using GeometryLibrary;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArgoPoint2D = ArgoParser.Point2D;
using ParisSection = ArgoParser.Geometry.Section;

namespace ArgoParser
{
    public class ArgoToPrssmConverter
    {
        private int _sectionIdCounter = 1;
        private int _materialIdCounter = 1;
        private string _sectionBaseName = "Сечение";

        public void SetSectionName(string argoFileName)
        {
            if (!string.IsNullOrEmpty(argoFileName))
            {
                _sectionBaseName = Path.GetFileNameWithoutExtension(argoFileName);
            }
        }

        public PrssmDocument Convert(ArgoDocument argoDoc)
        {
            if (argoDoc == null) throw new ArgumentNullException(nameof(argoDoc));

            if (!string.IsNullOrEmpty(argoDoc.SourceFileName))
            {
                _sectionBaseName = Path.GetFileNameWithoutExtension(argoDoc.SourceFileName);
            }

            var prssm = new PrssmDocument { BeamsNumber = argoDoc.GlobalParams.BeamCount };

            var material = ConvertMaterial(argoDoc.GlobalParams);
            double firstBeamCoord = argoDoc.GlobalParams.BeamCoordinates.FirstOrDefault();

            double midAxisZ = 0;
            if (argoDoc.GlobalParams.BeamCoordinates.Count > 0)
                midAxisZ = (argoDoc.GlobalParams.BeamCoordinates.First() + argoDoc.GlobalParams.BeamCoordinates.Last()) / 2.0;

            double extraOffset = 0;
            double prevRightEdge = double.MinValue;

            for (int i = 0; i < argoDoc.Beams.Count; i++)
            {
                var argoBeam = argoDoc.Beams[i];
                double beamAxisZ_cm = argoDoc.GlobalParams.BeamCoordinates[i];

                // 1) Контур + измененные точки
                var contour = ApplyChangedPoints(argoBeam);

                // 2) Перевод в мм относительно оси балки
                var pts = ConvertArgoContour(contour, beamAxisZ_cm);

                // 3) Зеркалирование
                bool desiredRight = beamAxisZ_cm > midAxisZ + 1e-6;
                bool needMirror = NeedMirrorByOverhang(pts, desiredRight, i, argoDoc.Beams.Count);

                if (needMirror)
                    pts = pts.Select(p => (-p.X, p.Y)).ToList();

                // 4) Границы профиля
                double profileMinX = pts.Min(p => p.X);
                double profileMaxX = pts.Max(p => p.X);
                double profileMinY = pts.Min(p => p.Y);

                // 5) Вычисляем ЦТ исходного профиля
                ComputeCentroid(pts, out double gcX_mm, out double gcY_mm);

                // 6) ЦЕНТРИРУЕМ ПРОФИЛЬ ПО ЦТ
                var profileCentered = pts
                    .Select(p => (X: p.X - gcX_mm, Y: p.Y - gcY_mm))
                    .ToList();

                // 7) ParisCadPoint2D из центрированного профиля
                var cadPoints = new List<ParisCadPoint2D>(profileCentered.Count);
                for (int k = 0; k < profileCentered.Count; k++)
                {
                    bool isProfilePoint = IsProfilePoint(profileCentered, k);
                    cadPoints.Add(new ParisCadPoint2D(
                        profileCentered[k].X,
                        profileCentered[k].Y,
                        isAnchor: false,
                        isProfile: isProfilePoint));
                }

                var region = new ParisRegion(cadPoints) { RegionType = RegionType.Body };

                string shapeName = $"{_sectionBaseName}_Б{i + 1}";

                var shape = new Geometry._BaseShape
                {
                    Id = 1,
                    Name = shapeName,
                    Beta = 0,
                    IsReflectX = false,
                    IsReflectY = false,
                    Location = new ParisCadPoint2D(0, 0),
                    Profile = new List<ParisRegion> { region }
                };

                var section = new ParisSection
                {
                    Id = _sectionIdCounter++,
                    Name = shapeName
                };
                section.Shapes.Add(shape);

                try
                {
                    section.Solve();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Beam #{i + 1}: Solve() ERROR: {ex.Message}");
                }

                // 8) Находим нижние углы РЕБРА по контуру
                double point4_X, point4_Y, point3_X, point3_Y;
                FindRibBottomCorners(profileCentered, out point4_X, out point4_Y, out point3_X, out point3_Y);

                // Ось ребра = середина между точками 3 и 4
                double ribAxisX_centered = (point3_X + point4_X) / 2.0;

                // Ширина ребра
                double ribWidth = Math.Abs(point3_X - point4_X);

                // OffsetY = смещение от оси ребра до ЦТ
                double offsetY = ribAxisX_centered;

                // OffsetZ = Y координата низа ребра
                double offsetZ = point4_Y;

                // 9) Находим верх ребра
                double ribTopY;
                var stressPoints = section.StressPoints;

                if (stressPoints != null && stressPoints.Count >= 4)
                {
                    double sp1Y = stressPoints[0].Y;
                    double sp2Y = stressPoints[1].Y;

                    // Если обе верхние точки примерно на одном уровне (однобалочное сечение с бортиками)
                    if (Math.Abs(sp1Y - sp2Y) < 50.0)
                    {
                        // Ищем верх ребра по центру ширины
                        ribTopY = FindRibTopAtCenter(profileCentered, ribAxisX_centered);
                    }
                    else
                    {
                        // Стандартный случай — берём меньшую Y (верх ребра, не бортик)
                        ribTopY = Math.Min(sp1Y, sp2Y);
                    }
                }
                else
                {
                    ribTopY = FindRibTopAtCenter(profileCentered, ribAxisX_centered);
                }

                // 10) Обновляем section.Yc/Zc
                section.Yc = Math.Round(offsetY / 1000.0, 5);
                section.Zc = Math.Round(-offsetZ / 1000.0, 5);

                // 11) Смещения профиля относительно оси ребра (для расчёта позиции балок)
                double ribAxisX_orig = gcX_mm + ribAxisX_centered;
                double profileLeftFromRib = profileMinX - ribAxisX_orig;
                double profileRightFromRib = profileMaxX - ribAxisX_orig;

                // 12) Базовая позиция оси ребра
                double axisPositionMm = (beamAxisZ_cm - firstBeamCoord) * 10.0;
                double positionMm = axisPositionMm + extraOffset;

                // 13) Глобальные координаты краёв профиля
                double currentLeftEdge = positionMm + profileLeftFromRib;
                double currentRightEdge = positionMm + profileRightFromRib;

                // 14) Проверяем наложение
                if (i > 0 && currentLeftEdge < prevRightEdge)
                {
                    double needed = prevRightEdge - currentLeftEdge;
                    extraOffset += needed;
                    positionMm += needed;
                    currentLeftEdge += needed;
                    currentRightEdge += needed;
                }

                prevRightEdge = currentRightEdge;

                double stepMm = 0;
                if (i > 0)
                    stepMm = positionMm - prssm.Beams[i - 1].Position;

                // 15) Границы центрированного профиля
                double profMinY = cadPoints.Min(p => p.Y);
                double profMaxY = cadPoints.Max(p => p.Y);

                // 16) PrssmSection
                var prssmSection = ConvertSectionToPrssm(section, shape, i, offsetY, offsetZ);

                // 17) Beam
                var prssmBeam = new PrssmBeam
                {
                    Id = i + 1,
                    Material = material,
                    Position = positionMm,
                    Step = stepMm,
                    BeamPartsNumber = 1
                };

                prssmBeam.BeamParts.Add(new PrssmBeamPart
                {
                    Id = 1,
                    Section = prssmSection,
                    Length = argoDoc.GlobalParams.FullLength * 10,
                    Division = 1,
                    IsStartPier = false,
                    IsEndPier = false
                });

                // 18) BindingPoint — локальные координаты точки 4 относительно точки привязки (низ-ось ребра)
                double bindingX_local = point4_X - ribAxisX_centered;
                double bindingY_local = 0;

                var bindingPointGlobal = new PrssmPoint(
                    x: Math.Round(positionMm + bindingX_local, 2),
                    y: Math.Round(bindingY_local, 2)
                );

                var llLocal = new ParisPoint2D(point4_X, point4_Y);

                // 19) Арматура
                ConvertLongitudinalReinforcement(
                    argoBeam, argoDoc, prssmBeam,
                    cadPoints,
                    bindingPointGlobal,
                    llLocal,
                    profMinY, ribTopY,
                    ribAxisX_centered,
                    ribWidth);

                ConvertTransverseReinforcement(
                    argoBeam, prssmBeam, cadPoints, bindingPointGlobal, llLocal, ribWidth, profMinY, ribTopY);

                prssmBeam.LongitudinalReinforcementNumber = prssmBeam.ReinforcementLongitudinals.Count;
                prssmBeam.TransverseReinforcementNumber = prssmBeam.ReinforcementTransverses.Count;

                prssm.Beams.Add(prssmBeam);
            }

            double slabWidthMm = 0;
            if (prssm.Beams.Count >= 2)
                slabWidthMm = prssm.Beams.Last().Position - prssm.Beams.First().Position;

            prssm.SelectedSlab = new PrssmSlab
            {
                Width = slabWidthMm > 0 ? slabWidthMm.ToString("F0") : "0"
            };

            return prssm;
        }

        #region Поиск верха ребра

        /// <summary>
        /// Находит верх ребра по центру ширины — ищет максимальную Y точки контура
        /// в районе оси ребра (центра нижней грани)
        /// </summary>
        private double FindRibTopAtCenter(List<(double X, double Y)> profileCentered, double ribAxisX)
        {
            // Ищем точки контура вблизи оси ребра (в пределах ±50мм от центра)
            const double tolerance = 50.0;

            var pointsNearAxis = profileCentered
                .Where(p => Math.Abs(p.X - ribAxisX) < tolerance)
                .ToList();

            if (pointsNearAxis.Count > 0)
            {
                // Берём максимальную Y среди точек вблизи оси
                return pointsNearAxis.Max(p => p.Y);
            }

            // Fallback: ищем по пересечению вертикальной линии X=ribAxisX с контуром
            double maxY = double.MinValue;

            for (int k = 0; k < profileCentered.Count; k++)
            {
                var p1 = profileCentered[k];
                var p2 = profileCentered[(k + 1) % profileCentered.Count];

                // Проверяем пересекает ли отрезок p1-p2 вертикальную линию X=ribAxisX
                if ((p1.X <= ribAxisX && p2.X >= ribAxisX) || (p1.X >= ribAxisX && p2.X <= ribAxisX))
                {
                    if (Math.Abs(p2.X - p1.X) > 0.01)
                    {
                        // Линейная интерполяция Y на X=ribAxisX
                        double t = (ribAxisX - p1.X) / (p2.X - p1.X);
                        double y = p1.Y + t * (p2.Y - p1.Y);
                        if (y > maxY)
                        {
                            maxY = y;
                        }
                    }
                }
            }

            if (maxY > double.MinValue)
            {
                return maxY;
            }

            // Крайний fallback — максимальная Y всего контура
            return profileCentered.Max(p => p.Y);
        }

        #endregion

        #region Поиск углов ребра

        /// <summary>
        /// Находит нижние углы ребра по контуру профиля.
        /// Ищет самую широкую горизонтальную линию на минимальном Y.
        /// </summary>
        private void FindRibBottomCorners(
            List<(double X, double Y)> profileCentered,
            out double point4_X, out double point4_Y,
            out double point3_X, out double point3_Y)
        {
            // Убираем дубликаты точек
            var uniquePoints = new List<(double X, double Y)>();
            for (int i = 0; i < profileCentered.Count; i++)
            {
                var p = profileCentered[i];
                if (uniquePoints.Count == 0 ||
                    Math.Abs(p.X - uniquePoints.Last().X) > 0.1 ||
                    Math.Abs(p.Y - uniquePoints.Last().Y) > 0.1)
                {
                    uniquePoints.Add(p);
                }
            }

            // Находим минимальный Y (низ профиля)
            double profMinY = uniquePoints.Min(p => p.Y);

            // Ищем горизонтальные линии на нижнем уровне
            var ribEdges = new List<(double X1, double X2, double Y)>();

            for (int k = 0; k < uniquePoints.Count; k++)
            {
                var curr = uniquePoints[k];
                var next = uniquePoints[(k + 1) % uniquePoints.Count];

                // Горизонтальная линия на нижнем уровне (с допуском)
                if (Math.Abs(curr.Y - profMinY) < 5.0 &&
                    Math.Abs(next.Y - profMinY) < 5.0 &&
                    Math.Abs(curr.Y - next.Y) < 1.0)
                {
                    double x1 = Math.Min(curr.X, next.X);
                    double x2 = Math.Max(curr.X, next.X);
                    double lineLen = x2 - x1;

                    if (lineLen > 10.0)  // Игнорируем очень короткие линии
                    {
                        ribEdges.Add((x1, x2, (curr.Y + next.Y) / 2.0));
                    }
                }
            }

            if (ribEdges.Count > 0)
            {
                // Берём самую широкую горизонтальную линию внизу - это низ ребра
                var widest = ribEdges.OrderByDescending(e => e.X2 - e.X1).First();
                point4_X = widest.X1;
                point3_X = widest.X2;
                point4_Y = widest.Y;
                point3_Y = widest.Y;
            }
            else
            {
                // Fallback - берём крайние точки на минимальном Y
                var bottomPoints = uniquePoints
                    .Where(p => Math.Abs(p.Y - profMinY) < 5.0)
                    .OrderBy(p => p.X)
                    .ToList();

                if (bottomPoints.Count >= 2)
                {
                    point4_X = bottomPoints.First().X;
                    point4_Y = bottomPoints.First().Y;
                    point3_X = bottomPoints.Last().X;
                    point3_Y = bottomPoints.Last().Y;
                }
                else
                {
                    // Крайний fallback
                    point4_X = uniquePoints.Min(p => p.X);
                    point3_X = uniquePoints.Max(p => p.X);
                    point4_Y = profMinY;
                    point3_Y = profMinY;
                }
            }
        }

        #endregion

        #region Центроид

        private void ComputeCentroid(List<(double X, double Y)> pts, out double cx, out double cy)
        {
            double signedArea = 0;
            cx = 0;
            cy = 0;
            int n = pts.Count;

            for (int j = 0; j < n; j++)
            {
                int k = (j + 1) % n;
                double cross = pts[j].X * pts[k].Y - pts[k].X * pts[j].Y;
                signedArea += cross;
                cx += (pts[j].X + pts[k].X) * cross;
                cy += (pts[j].Y + pts[k].Y) * cross;
            }

            signedArea /= 2.0;
            if (Math.Abs(signedArea) < 1e-10)
            {
                cx = 0;
                cy = 0;
                return;
            }

            cx /= (6.0 * signedArea);
            cy /= (6.0 * signedArea);
        }

        private bool NeedMirrorByOverhang(List<(double X, double Y)> pts, bool desiredRight, int beamIndex, int totalBeams)
        {
            // Для крайних балок определяем по позиции относительно центра
            // Первая балка — свес должен быть слева (desiredRight = false)
            // Последняя балка — свес должен быть справа (desiredRight = true)

            double minX = pts.Min(p => p.X);
            double maxX = pts.Max(p => p.X);

            double leftOverhang = -minX;  // насколько выступает влево от оси
            double rightOverhang = maxX;  // насколько выступает вправо от оси

            // Если разница свесов маленькая — сечение почти симметричное
            if (Math.Abs(rightOverhang - leftOverhang) < 50)
            {
                // Для симметричных сечений не зеркалируем
                return false;
            }

            // Определяем куда сейчас выступает сечение
            bool currentlyRight = rightOverhang > leftOverhang;

            // Зеркалируем если текущее направление не совпадает с желаемым
            return currentlyRight != desiredRight;
        }

        private double DetectZScale(List<double> zCoords, double ribWidthMm)
        {
            if (zCoords == null || zCoords.Count < 2) return 10.0;
            if (ribWidthMm <= 1e-6) return 10.0;

            double range = zCoords.Max() - zCoords.Min();

            double d1 = Math.Abs(range - ribWidthMm);
            double d10 = Math.Abs(range * 10.0 - ribWidthMm);

            return (d10 < d1) ? 10.0 : 1.0;
        }

        #endregion

        #region Контур

        private List<ArgoPoint2D> ApplyChangedPoints(Beam argoBeam)
        {
            var contour = argoBeam.CrossSectionContour
                .Select(p => new ArgoPoint2D(p.Z, p.Y)).ToList();

            if (argoBeam.ChangedPointsCount > 0)
            {
                for (int j = 0; j < argoBeam.ChangedPointsCount; j++)
                {
                    int idx = argoBeam.ChangedPointIndices[j] - 1;
                    if (idx >= 0 && idx < contour.Count)
                    {
                        contour[idx] = new ArgoPoint2D(
                            argoBeam.ChangedPoints[j].Z,
                            argoBeam.ChangedPoints[j].Y);
                    }
                }
            }

            // Убираем дубликаты точек (соседние точки с одинаковыми координатами)
            var filtered = new List<ArgoPoint2D>();
            for (int i = 0; i < contour.Count; i++)
            {
                var p = contour[i];
                if (filtered.Count == 0 ||
                    Math.Abs(p.Z - filtered.Last().Z) > 0.1 ||
                    Math.Abs(p.Y - filtered.Last().Y) > 0.1)
                {
                    filtered.Add(p);
                }
            }

            // Также проверяем замыкание контура (последняя точка != первая)
            if (filtered.Count > 1)
            {
                var first = filtered.First();
                var last = filtered.Last();
                if (Math.Abs(first.Z - last.Z) < 0.1 && Math.Abs(first.Y - last.Y) < 0.1)
                {
                    filtered.RemoveAt(filtered.Count - 1);
                }
            }

            return filtered;
        }

        private List<(double X, double Y)> ConvertArgoContour(
            List<ArgoPoint2D> argoPoints, double beamAxisZ)
        {
            var result = new List<(double X, double Y)>();
            foreach (var p in argoPoints)
            {
                double x = (p.Z - beamAxisZ) * 10;
                double y = p.Y * 10;
                result.Add((x, y));
            }
            return result;
        }

        private bool IsProfilePoint(List<(double X, double Y)> pts, int k)
        {
            if (k == 0 || k == pts.Count - 1) return true;
            var a = pts[k - 1];
            var b = pts[k];
            var c = pts[k + 1];
            double cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            return Math.Abs(cross) >= 0.5;
        }

        #endregion

        #region Section → PrssmSection

        private PrssmSection ConvertSectionToPrssm(
            ParisSection section,
            Geometry._BaseShape shape,
            int beamIndex,
            double offsetY,
            double offsetZ)
        {
            string sectionName = $"{_sectionBaseName}_Б{beamIndex + 1}";

            var prssmSection = new PrssmSection
            {
                Id = section.Id,
                Name = sectionName,
                SectionType = 1,
                Yc = section.Yc,
                Zc = section.Zc,
                Perimeter = section.Perimeter,
                Area = section.Area,
                Iyy = section.Iyy,
                Izz = section.Izz,
                Iyz = section.Iyz,
                It = section.It,
                Syy = section.Syy,
                Szz = section.Szz,
                Byy = section.Byy,
                Bzz = section.Bzz,
                WyyPlus = section.WyyPlus,
                WyyMinus = section.WyyMinus,
                WzzPlus = section.WzzPlus,
                WzzMinus = section.WzzMinus,
                OffsetType = 11,
                OffsetY = Math.Round(offsetY, 2),
                OffsetZ = Math.Round(offsetZ, 2),
                WeightFactor = section.WeightFactor
            };

            var prssmShape = new PrssmShape
            {
                Id = 1,
                Name = "custom",
                CadType = 0,
                Beta = shape.Beta,
                IsReflectX = shape.IsReflectX,
                IsReflectY = shape.IsReflectY,
                Location = new PrssmPoint(shape.Location.X, shape.Location.Y)
            };

            foreach (var region in shape.Profile)
            {
                var prssmRegion = new PrssmProfileRegion
                {
                    RegionType = (int)region.RegionType,
                    Points = new List<PrssmProfilePoint>()
                };

                foreach (var p in region.Points)
                {
                    prssmRegion.Points.Add(new PrssmProfilePoint
                    {
                        X = Math.Round(p.X, 2),
                        Y = Math.Round(p.Y, 2),
                        IsAnchor = false,
                        IsProfilePoint = p.IsProfilePoint
                    });
                }

                prssmShape.Profile.Add(prssmRegion);
            }

            prssmSection.Shapes.Add(prssmShape);

            prssmSection.StressPoints = section.StressPoints
                .Select(p => new PrssmPoint(Math.Round(p.X, 2), Math.Round(p.Y, 2)))
                .ToList();

            return prssmSection;
        }

        #endregion

        #region Материал

        private PrssmMaterial ConvertMaterial(GlobalParameters gp)
        {
            var (name, youngModulus, stdType) = GetConcreteClass(gp.ConcreteStrength);
            return new PrssmMaterial
            {
                Id = _materialIdCounter++,
                Name = name,
                MaterialType = 0,
                StandartMaterialType = stdType,
                YoungModulus = youngModulus,
                PoissonRatio = 0.2,
                SpecificWeight = 2.45E-05,
                ThermalCoefficient = 1E-05
            };
        }

        private (string Name, double YoungModulus, int StdType) GetConcreteClass(double strength)
        {
            if (strength <= 7.5) return ("Б7.5", 16000, 8);
            if (strength <= 10) return ("Б10", 18000, 9);
            if (strength <= 12.5) return ("Б12.5", 21000, 10);
            if (strength <= 15) return ("Б15", 23000, 11);
            if (strength <= 17.5) return ("Б17.5", 25500, 12);
            if (strength <= 20) return ("Б20", 27000, 13);
            if (strength <= 22.5) return ("Б22.5", 28500, 14);
            if (strength <= 25) return ("Б25", 30000, 15);
            if (strength <= 27.5) return ("Б27.5", 31000, 16);
            if (strength <= 30) return ("Б30", 32500, 17);
            if (strength <= 35) return ("Б35", 34500, 18);
            if (strength <= 40) return ("Б40", 36000, 19);
            if (strength <= 45) return ("Б45", 37000, 20);
            if (strength <= 50) return ("Б50", 38000, 21);
            if (strength <= 55) return ("Б55", 39000, 22);
            return ("Б60", 39500, 23);
        }

        #endregion

        #region Продольная арматура

        private void ConvertLongitudinalReinforcement(
            Beam argoBeam, ArgoDocument argoDoc,
            PrssmBeam prssmBeam,
            List<ParisCadPoint2D> profilePoints,
            PrssmPoint bindingPointGlobal,
            ParisPoint2D llLocal,
            double profMinY, double ribTopY,
            double ribAxisXLocal,
            double ribWidth)
        {
            var detailed = argoDoc.DetailedReinforcement?.BeamDetails
                .FirstOrDefault(d => d.BeamNumber == argoBeam.Number);

            if (detailed == null || (detailed.TensileBars.Count == 0 && detailed.CompressedBars.Count == 0))
            {
                ConvertLongFromCalc(argoBeam, prssmBeam, llLocal, bindingPointGlobal,
                    profMinY, ribTopY, ribAxisXLocal, ribWidth);
                return;
            }

            double ribWidthMm = ribWidth;

            // РАСТЯНУТЫЕ
            for (int j = 0; j < detailed.TensileBars.Count && j < argoBeam.TensileBars.Count; j++)
            {
                var dg = detailed.TensileBars[j];
                var cb = argoBeam.TensileBars[j];
                if (dg.Count <= 0) continue;

                double yFirstAbs = 0;
                double stepElement = 0;

                if (dg.ZCoordinates.Count > 0)
                {
                    var sortedZ = dg.ZCoordinates.OrderBy(z => z).ToList();
                    double zScale = DetectZScale(sortedZ, ribWidthMm);

                    double firstX = ribAxisXLocal + sortedZ.First() * zScale;
                    double lastX = ribAxisXLocal + sortedZ.Last() * zScale;

                    yFirstAbs = firstX;
                    stepElement = (dg.Count > 1) ? (lastX - firstX) / (dg.Count - 1) : 0;
                }

                double zAbs = profMinY + (cb.DeltaLower * 10);

                double yOffset = yFirstAbs - llLocal.X;
                double zOffset = zAbs - llLocal.Y;

                prssmBeam.ReinforcementLongitudinals.Add(new PrssmLongitudinalReinforcement
                {
                    Diameter = dg.Diameter,
                    ItemsAtRow = dg.Count,
                    NAtItem = 1,
                    BindingPoint = bindingPointGlobal,
                    YOffset = Math.Round(yOffset, 2),
                    ZOffset = Math.Round(zOffset, 2),
                    StepElement = Math.Round(stepElement, 2),
                    OffsetFromStart = Math.Max(0, cb.XMin * 10),
                    SegmentCount = 1,
                    Segments = new List<PrssmReinforcementSegment>
                    {
                        new PrssmReinforcementSegment { Length = (cb.XMax - cb.XMin) * 10 }
                    }
                });
            }

            // СЖАТЫЕ
            for (int j = 0; j < detailed.CompressedBars.Count && j < argoBeam.CompressedBars.Count; j++)
            {
                var dg = detailed.CompressedBars[j];
                var cb = argoBeam.CompressedBars[j];
                if (dg.Count <= 0) continue;

                double yFirstAbs = 0;
                double stepElement = 0;

                if (dg.ZCoordinates.Count > 0)
                {
                    var sortedZ = dg.ZCoordinates.OrderBy(z => z).ToList();
                    double zScale = DetectZScale(sortedZ, ribWidthMm);

                    double firstX = ribAxisXLocal + sortedZ.First() * zScale;
                    double lastX = ribAxisXLocal + sortedZ.Last() * zScale;

                    yFirstAbs = firstX;
                    stepElement = (dg.Count > 1) ? (lastX - firstX) / (dg.Count - 1) : 0;
                }

                // Для сжатой арматуры - от верха РЕБРА (не всего профиля!)
                double zAbs = ribTopY - (cb.DeltaUpper * 10);

                double yOffset = yFirstAbs - llLocal.X;
                double zOffset = zAbs - llLocal.Y;

                prssmBeam.ReinforcementLongitudinals.Add(new PrssmLongitudinalReinforcement
                {
                    Diameter = dg.Diameter,
                    ItemsAtRow = dg.Count,
                    NAtItem = 1,
                    BindingPoint = bindingPointGlobal,
                    YOffset = Math.Round(yOffset, 2),
                    ZOffset = Math.Round(zOffset, 2),
                    StepElement = Math.Round(stepElement, 2),
                    OffsetFromStart = Math.Max(0, cb.XMin * 10),
                    SegmentCount = 1,
                    Segments = new List<PrssmReinforcementSegment>
                    {
                        new PrssmReinforcementSegment { Length = (cb.XMax - cb.XMin) * 10 }
                    }
                });
            }
        }

        private void ConvertLongFromCalc(
            Beam argoBeam,
            PrssmBeam prssmBeam,
            ParisPoint2D llLocal,
            PrssmPoint bindingPointGlobal,
            double profMinY,
            double ribTopY,
            double ribAxisXLocal,
            double ribWidth)
        {
            foreach (var cb in argoBeam.TensileBars)
            {
                if (cb.Area <= 0) continue;
                var (d, n) = EstDiamCount(cb.Area);

                double usableWidth = Math.Max(0, ribWidth - 2 * 25 - d);
                double stepEl = (n > 1) ? usableWidth / (n - 1) : 0;

                double firstX = ribAxisXLocal - usableWidth / 2.0;
                double zAbs = profMinY + (cb.DeltaLower * 10);

                prssmBeam.ReinforcementLongitudinals.Add(new PrssmLongitudinalReinforcement
                {
                    Diameter = d,
                    ItemsAtRow = n,
                    NAtItem = 1,
                    BindingPoint = bindingPointGlobal,
                    YOffset = Math.Round(firstX - llLocal.X, 2),
                    ZOffset = Math.Round(zAbs - llLocal.Y, 2),
                    StepElement = Math.Round(stepEl, 2),
                    OffsetFromStart = Math.Max(0, cb.XMin * 10),
                    SegmentCount = 1,
                    Segments = new List<PrssmReinforcementSegment>
                    {
                        new PrssmReinforcementSegment { Length = (cb.XMax - cb.XMin) * 10 }
                    }
                });
            }

            foreach (var cb in argoBeam.CompressedBars)
            {
                if (cb.Area <= 0) continue;
                var (d, n) = EstDiamCount(cb.Area);

                double usableWidth = Math.Max(0, ribWidth - 2 * 25 - d);
                double stepEl = (n > 1) ? usableWidth / (n - 1) : 0;

                double firstX = ribAxisXLocal - usableWidth / 2.0;
                // Для сжатой арматуры - от верха РЕБРА
                double zAbs = ribTopY - (cb.DeltaUpper * 10);

                prssmBeam.ReinforcementLongitudinals.Add(new PrssmLongitudinalReinforcement
                {
                    Diameter = d,
                    ItemsAtRow = n,
                    NAtItem = 1,
                    BindingPoint = bindingPointGlobal,
                    YOffset = Math.Round(firstX - llLocal.X, 2),
                    ZOffset = Math.Round(zAbs - llLocal.Y, 2),
                    StepElement = Math.Round(stepEl, 2),
                    OffsetFromStart = Math.Max(0, cb.XMin * 10),
                    SegmentCount = 1,
                    Segments = new List<PrssmReinforcementSegment>
                    {
                        new PrssmReinforcementSegment { Length = (cb.XMax - cb.XMin) * 10 }
                    }
                });
            }
        }

        #endregion

        #region Поперечная арматура

        private void ConvertTransverseReinforcement(
            Beam argoBeam, PrssmBeam prssmBeam,
            IEnumerable<ParisCadPoint2D> profilePoints,
            PrssmPoint bindingPoint,
            ParisPoint2D llLocal,
            double ribWidth,
            double profMinY,
            double ribTopY)
        {
            if (argoBeam.StirrupSections.Count == 0) return;

            // Высота ребра (от низа до верха ребра, не плиты!)
            double ribH = ribTopY - profMinY;

            const double coverSide = 25;
            const double coverBottom = 25;

            double startX = 0;

            foreach (var ss in argoBeam.StirrupSections)
            {
                if (ss.Area <= 0 || ss.Step <= 0)
                {
                    startX = ss.EndX * 10;
                    continue;
                }

                double d = EstStirrupDiam(ss.Area);
                double secLen = (ss.EndX * 10) - startX;
                int cnt = (int)Math.Max(1, secLen / (ss.Step * 10));

                double stirrupW = ribWidth - 2 * coverSide;
                double stirrupH = ribH - 2 * coverBottom;

                prssmBeam.ReinforcementTransverses.Add(
                    new PrssmTransverseReinforcement
                    {
                        Diameter = d,
                        ItemsAtRow = cnt,
                        NAtItem = 1,
                        IsClosed = false,
                        StepElement = ss.Step * 10,
                        OffsetFromStart = startX,
                        YOffset = 0,
                        ZOffset = 0,
                        BindingPoint = bindingPoint,
                        SegmentCount = 2,
                        Segments = new List<PrssmReinforcementSegment>
                        {
                            new PrssmReinforcementSegment { Length = stirrupW, Angle = 0 },
                            new PrssmReinforcementSegment { Length = stirrupH, Angle = 90, Height = stirrupH }
                        }
                    });
                startX = ss.EndX * 10;
            }
        }

        #endregion

        #region Утилиты

        private (double Diameter, int Count) EstDiamCount(double areaCm2)
        {
            int[] diameters = { 6, 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 32, 36, 40 };
            double areaMm2 = areaCm2 * 100;
            foreach (var d in diameters.Reverse())
            {
                double barArea = Math.PI * d * d / 4;
                int n = (int)Math.Round(areaMm2 / barArea);
                if (n >= 1 && n <= 10) return (d, n);
            }
            return (16, Math.Max(1, (int)Math.Round(areaMm2 / (Math.PI * 256 / 4))));
        }

        private double EstStirrupDiam(double areaCm2)
        {
            double areaMm2 = areaCm2 * 100 / 2;
            foreach (var d in new[] { 6, 8, 10, 12, 14 })
                if (Math.PI * d * d / 4 >= areaMm2 * 0.8) return d;
            return 10;
        }

        #endregion
    }
}