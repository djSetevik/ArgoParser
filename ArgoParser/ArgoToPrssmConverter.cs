using GeometryLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using ArgoPoint2D = ArgoParser.Point2D;
using ParisSection = ArgoParser.Geometry.Section;

namespace ArgoParser
{
    public class ArgoToPrssmConverter
    {
        private int _sectionIdCounter = 1;
        private int _materialIdCounter = 1;

        public PrssmDocument Convert(ArgoDocument argoDoc)
        {
            if (argoDoc == null) throw new ArgumentNullException(nameof(argoDoc));

            var prssm = new PrssmDocument { BeamsNumber = argoDoc.GlobalParams.BeamCount };

            var material = ConvertMaterial(argoDoc.GlobalParams);
            double firstBeamCoord = argoDoc.GlobalParams.BeamCoordinates.FirstOrDefault();

            double midAxisZ = 0;
            if (argoDoc.GlobalParams.BeamCoordinates.Count > 0)
                midAxisZ = (argoDoc.GlobalParams.BeamCoordinates.First() + argoDoc.GlobalParams.BeamCoordinates.Last()) / 2.0;

            for (int i = 0; i < argoDoc.Beams.Count; i++)
            {
                var argoBeam = argoDoc.Beams[i];
                double beamAxisZ_cm = argoDoc.GlobalParams.BeamCoordinates[i];

                // Position/Step (мм)
                double positionMm = (beamAxisZ_cm - firstBeamCoord) * 10.0;
                double stepMm = 0;
                if (i > 0)
                {
                    double prev = argoDoc.GlobalParams.BeamCoordinates[i - 1];
                    stepMm = (beamAxisZ_cm - prev) * 10.0;
                }

                // 1) Контур + измененные точки
                var contour = ApplyChangedPoints(argoBeam);

                // 2) Перевод в мм относительно оси балки (ось балки => X=0)
                var pts = ConvertArgoContour(contour, beamAxisZ_cm); // List<(Xmm,Ymm)>

                // 3) Решаем, нужно ли зеркалить (без сравнения с первой балкой)
                bool desiredRight = beamAxisZ_cm > midAxisZ + 1e-6;
                bool needMirror = NeedMirrorByOverhang(pts, desiredRight);

                if (needMirror)
                    pts = pts.Select(p => (-p.X, p.Y)).ToList();

                // 4) ЦТ исходного профиля (мм) — в ЭТОЙ же системе что pts
                ComputeCentroid(pts, out double gcX_mm, out double gcY_mm);

                // 5) КЛЮЧЕВО: профиль, который пишем в JSON, должен быть центрирован по ЦТ
                // иначе уедут силовые точки относительно контура.
                var profile = pts
                    .Select(p => (X: p.X - gcX_mm, Y: p.Y - gcY_mm))
                    .ToList();

                // 6) ParisCadPoint2D
                var cadPoints = new List<ParisCadPoint2D>(profile.Count);
                for (int k = 0; k < profile.Count; k++)
                {
                    bool isProfilePoint = IsProfilePoint(profile, k);
                    cadPoints.Add(new ParisCadPoint2D(profile[k].X, profile[k].Y, isAnchor: true, isProfile: isProfilePoint));
                }

                var region = new ParisRegion(cadPoints) { RegionType = RegionType.Body };

                // 7) Shape/Section
                string shapeName = needMirror
                    ? "Ж/б двутавровая балка несимметричная - копия"
                    : "Ж/б двутавровая балка несимметричная";

                var shape = new Geometry._BaseShape
                {
                    Id = 1,
                    Name = shapeName,
                    Beta = 0,
                    IsReflectX = false,

                    // ВАЖНО: мы уже отразили точки => IsReflectY=false
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

                // 8) OffsetZ = minY профиля (мм) — строго так
                var profPts = shape.Profile.First().Points.ToList();
                double profMinY = profPts.Min(p => p.Y);
                double profMaxY = profPts.Max(p => p.Y);

                section.OffsetY = 0;
                section.OffsetZ = Math.Round(profMinY, 2);

                // 9) Yc/Zc в метрах — это ЦТ исходного НЕцентрированного профиля относительно оси балки
                // (как у вас было в удачных версиях)
                section.Yc = Math.Round(gcX_mm / 1000.0, 5);
                section.Zc = Math.Round(gcY_mm / 1000.0, 5);

                // 10) PrssmSection
                var prssmSection = ConvertSectionToPrssm(section, shape);

                // 11) Beam
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

                // 12) Нижняя-левая силовая точка в ЛОКАЛЬНЫХ координатах профиля
                var llLocal = GetLowerLeftStressPoint(section, cadPoints);

                // BindingPoint (как у ПАРИС в эталоне) = Position + llLocal.X, llLocal.Y
                var bindingPointGlobal = new PrssmPoint(
                    x: Math.Round(positionMm + llLocal.X, 2),
                    y: Math.Round(llLocal.Y, 2)
                );

                // 13) Ось ребра в локальных координатах (по низу профиля)
                double ribAxisXLocal = ComputeRibAxisX(cadPoints);

                // 14) Арматура
                ConvertLongitudinalReinforcement(
                    argoBeam, argoDoc, prssmBeam,
                    cadPoints,
                    bindingPointGlobal,
                    llLocal,
                    profMinY, profMaxY,
                    ribAxisXLocal);

                ConvertTransverseReinforcement(
                    argoBeam, prssmBeam, cadPoints, bindingPointGlobal);

                prssmBeam.LongitudinalReinforcementNumber = prssmBeam.ReinforcementLongitudinals.Count;
                prssmBeam.TransverseReinforcementNumber = prssmBeam.ReinforcementTransverses.Count;

                prssm.Beams.Add(prssmBeam);

                // Диагностика (можно временно оставить)
                Console.WriteLine($"Beam {i + 1}: profMinY={profMinY:F2}, OffsetZ={section.OffsetZ:F2}, " +
                                  $"LL=({llLocal.X:F2},{llLocal.Y:F2}), StressBottomY=" +
                                  $"{(section.StressPoints.Count >= 4 ? section.StressPoints[2].Y.ToString("F2") : "n/a")}");
            }

            // Slab.Width (мм)
            double slabWidthMm = 0;
            if (argoDoc.GlobalParams.BeamCoordinates.Count >= 2)
                slabWidthMm = (argoDoc.GlobalParams.BeamCoordinates.Last() - argoDoc.GlobalParams.BeamCoordinates.First()) * 10;

            prssm.SelectedSlab = new PrssmSlab
            {
                Width = slabWidthMm > 0 ? slabWidthMm.ToString("F0") : "0"
            };

            return prssm;
        }

        #region Центроид

        /// <summary>
        /// Вычисление ЦТ полигона (формула Shoelace)
        /// </summary>
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
        private ParisPoint2D GetLowerLeftStressPoint(ParisSection section, List<ParisCadPoint2D> profilePoints)
        {
            if (section.StressPoints != null && section.StressPoints.Count > 0)
            {
                var ll = section.StressPoints.OrderBy(p => p.Y).ThenBy(p => p.X).First();
                return new ParisPoint2D(ll.X, ll.Y);
            }

            // fallback: по профилю
            var pll = profilePoints.OrderBy(p => p.Y).ThenBy(p => p.X).First();
            return new ParisPoint2D(pll.X, pll.Y);
        }

        private double ComputeRibAxisX(List<ParisCadPoint2D> pts)
        {
            double minY = pts.Min(p => p.Y);
            var bottom = pts.Where(p => Math.Abs(p.Y - minY) < 1.0).ToList();
            if (bottom.Count >= 2)
                return (bottom.Min(p => p.X) + bottom.Max(p => p.X)) / 2.0;

            return 0;
        }

        private double EstimateRibWidthAtBottom(List<ParisCadPoint2D> pts)
        {
            double minY = pts.Min(p => p.Y);
            var bottom = pts.Where(p => Math.Abs(p.Y - minY) < 1.0).ToList();
            if (bottom.Count >= 2)
                return bottom.Max(p => p.X) - bottom.Min(p => p.X);

            return pts.Max(p => p.X) - pts.Min(p => p.X);
        }

        /// <summary>
        /// Определяет, нужно ли зеркалить, исходя из "вылета" сечения вправо/влево.
        /// desiredRight=true => балка должна иметь больший вылет вправо.
        /// </summary>
        private bool NeedMirrorByOverhang(List<(double X, double Y)> pts, bool desiredRight)
        {
            double minX = pts.Min(p => p.X);
            double maxX = pts.Max(p => p.X);

            double left = -minX;
            double right = maxX;

            // если почти симметрия — зеркалить не нужно (и не важно)
            if (Math.Abs(right - left) < 1e-3)
                return false;

            bool actualRight = right > left;
            return actualRight != desiredRight;
        }

        /// <summary>
        /// Масштаб Z-координат фактических стержней: иногда они приходят как "-18..18" (см), тогда надо *10.
        /// Иногда могут быть уже в мм. Выбираем, что ближе к ширине ребра.
        /// </summary>
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
            return contour;
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

        private void FindRibGeometry(IEnumerable<ParisCadPoint2D> points,
            out double ribCenterX, out double ribLeftX)
        {
            double profMinY = points.Min(p => p.Y);
            var bottomPts = points.Where(p => Math.Abs(p.Y - profMinY) < 1).ToList();
            ribLeftX = bottomPts.Any() ? bottomPts.Min(p => p.X) : 0;
            double ribRightX = bottomPts.Any() ? bottomPts.Max(p => p.X) : 0;
            ribCenterX = (ribLeftX + ribRightX) / 2.0;
        }

        #endregion

        #region Section → PrssmSection

        private PrssmSection ConvertSectionToPrssm(
            ParisSection section,
            Geometry._BaseShape shape)
        {
            var prssmSection = new PrssmSection
            {
                Id = section.Id,
                Name = section.Name,
                SectionType = 0,
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
                OffsetType = 8,
                OffsetY = section.OffsetY,
                OffsetZ = section.OffsetZ,
                WeightFactor = section.WeightFactor
            };

            var prssmShape = new PrssmShape
            {
                Id = 1,
                Name = section.Name,
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
                        IsAnchor = p.IsAnchor,
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

        #region Зеркалирование

        private bool DetectMirror(
            List<(double X, double Y)> c1, List<(double X, double Y)> c2)
        {
            if (c1.Count != c2.Count) return false;
            int matched = 0;
            foreach (var p in c2)
            {
                if (c1.Any(q => Math.Abs(-q.X - p.X) < 5 &&
                                Math.Abs(q.Y - p.Y) < 5))
                    matched++;
            }
            return matched > c2.Count * 0.8;
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
    double profMinY, double profMaxY,
    double ribAxisXLocal
)
        {
            var detailed = argoDoc.DetailedReinforcement?.BeamDetails
                .FirstOrDefault(d => d.BeamNumber == argoBeam.Number);

            if (detailed == null || (detailed.TensileBars.Count == 0 && detailed.CompressedBars.Count == 0))
            {
                ConvertLongFromCalc(argoBeam, prssmBeam, llLocal, bindingPointGlobal, profMinY, profMaxY, ribAxisXLocal, profilePoints);
                return;
            }

            double ribWidthMm = EstimateRibWidthAtBottom(profilePoints);

            var (ribBottomY, ribTopY) = GetBottomTopAtRibAxis(profilePoints, ribAxisXLocal, profMinY, profMaxY);
            // ---- РАСТЯНУТЫЕ ----
            for (int j = 0; j < detailed.TensileBars.Count && j < argoBeam.TensileBars.Count; j++)
            {
                var dg = detailed.TensileBars[j];
                var cb = argoBeam.TensileBars[j];
                if (dg.Count <= 0) continue;

                double yFirstAbs = 0;   // X в локальных координатах профиля (мм)
                double stepElement = 0;

                if (dg.ZCoordinates.Count > 0)
                {
                    var sortedZ = dg.ZCoordinates.OrderBy(z => z).ToList();
                    double zScale = DetectZScale(sortedZ, ribWidthMm); // 1 или 10

                    double firstX = ribAxisXLocal + sortedZ.First() * zScale;
                    double lastX = ribAxisXLocal + sortedZ.Last() * zScale;

                    yFirstAbs = firstX; // первый слева
                    stepElement = (dg.Count > 1) ? (lastX - firstX) / (dg.Count - 1) : 0;
                }

                // ВАЖНО: DeltaLower в см -> *10 мм, И ЭТО УЖЕ КООРДИНАТА ОСИ!
                // Поэтому НЕ добавляем dg.Diameter/2.
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

                // ВОТ КЛЮЧЕВОЕ ИЗМЕНЕНИЕ:
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
    double profMaxY,
    double ribAxisXLocal,
    List<ParisCadPoint2D> profilePoints)
        {
            double ribWidth = EstimateRibWidthAtBottom(profilePoints);

            foreach (var cb in argoBeam.TensileBars)
            {
                if (cb.Area <= 0) continue;
                var (d, n) = EstDiamCount(cb.Area);

                double usableWidth = Math.Max(0, ribWidth - 2 * 25 - d);
                double stepEl = (n > 1) ? usableWidth / (n - 1) : 0;

                double firstX = ribAxisXLocal - usableWidth / 2.0;

                // DeltaLower в см -> мм. Это ось -> НЕ +d/2
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

                // DeltaUpper в см -> мм. Это ось -> НЕ -d/2
                double zAbs = profMaxY - (cb.DeltaUpper * 10);

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
            PrssmPoint bindingPoint)
        {
            if (argoBeam.StirrupSections.Count == 0) return;

            double profMinY = profilePoints.Min(p => p.Y);
            double profMaxY = profilePoints.Max(p => p.Y);
            double h = profMaxY - profMinY;
            double ribW = EstRibWidth(profilePoints);
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
                    new PrssmReinforcementSegment { Length = ribW - 60, Angle = 0 },
                    new PrssmReinforcementSegment { Length = h - 40, Angle = 90, Height = h - 40 }
                        }
                    });
                startX = ss.EndX * 10;
            }
        }

        #endregion

        #region Утилиты
        private static List<double> IntersectionsWithVerticalLine(IReadOnlyList<ParisCadPoint2D> poly, double x, double eps = 1e-9)
        {
            var ys = new List<double>();
            int n = poly.Count;
            if (n < 2) return ys;

            for (int i = 0; i < n; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % n];

                double x1 = a.X, y1 = a.Y;
                double x2 = b.X, y2 = b.Y;

                // Вертикальный отрезок
                if (Math.Abs(x2 - x1) < eps)
                {
                    if (Math.Abs(x - x1) < eps)
                    {
                        ys.Add(y1);
                        ys.Add(y2);
                    }
                    continue;
                }

                // Проверяем пересечение по X
                bool crosses = (x >= Math.Min(x1, x2) - eps) && (x <= Math.Max(x1, x2) + eps);
                if (!crosses) continue;

                double t = (x - x1) / (x2 - x1);
                if (t < -eps || t > 1 + eps) continue;

                double y = y1 + t * (y2 - y1);
                ys.Add(y);
            }

            // Уберём дубли от “касания вершиной”
            ys = ys.Select(v => Math.Round(v, 6)).Distinct().OrderBy(v => v).ToList();
            return ys;
        }

        private static (double bottomY, double topY) GetBottomTopAtRibAxis(
            List<ParisCadPoint2D> profilePoints,
            double ribAxisXLocal,
            double fallbackMinY,
            double fallbackMaxY)
        {
            var ys = IntersectionsWithVerticalLine(profilePoints, ribAxisXLocal);

            if (ys.Count >= 2)
                return (ys.First(), ys.Last());

            // fallback если почему-то не нашли пересечения
            return (fallbackMinY, fallbackMaxY);
        }

        private static PrssmPoint GetBindingPointFromStressPoints(ParisSection section, IEnumerable<ParisCadPoint2D> profilePoints)
        {
            // 1) Предпочтительно: нижняя-левая из StressPoints
            if (section.StressPoints != null && section.StressPoints.Count > 0)
            {
                var ll = section.StressPoints
                    .OrderBy(p => p.Y)   // ниже
                    .ThenBy(p => p.X)    // левее
                    .First();

                return new PrssmPoint(Math.Round(ll.X, 2), Math.Round(ll.Y, 2));
            }

            // 2) fallback: нижняя-левая точка профиля
            var pll = profilePoints
                .OrderBy(p => p.Y)
                .ThenBy(p => p.X)
                .First();

            return new PrssmPoint(Math.Round(pll.X, 2), Math.Round(pll.Y, 2));
        }
        private double EstRibWidth(IEnumerable<ParisCadPoint2D> pts)
        {
            double midY = (pts.Min(p => p.Y) + pts.Max(p => p.Y)) / 2;
            var lowerHalf = pts.Where(p => p.Y < midY).ToList();
            if (lowerHalf.Count < 2)
                return pts.Max(p => p.X) - pts.Min(p => p.X);
            return lowerHalf.Max(p => p.X) - lowerHalf.Min(p => p.X);
        }

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