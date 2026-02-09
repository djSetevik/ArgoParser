using CrossSection;
using CrossSection.Analysis;
using CrossSection.DataModel;
using GeometryLibrary;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Point2D = CrossSection.Point2D;

namespace ArgoParser.Geometry
{
    /// <summary>
    /// Класс сечения элемента
    /// </summary>
    public class Section : INotifyPropertyChanged
    {

        #region Fields
        private string name = "";
        private double yc;
        private double zc;
        private double area;
        private double perimeter;
        private double iyy;
        private double izz;
        private double iyz;
        private double it;
        private double syy;
        private double szz;
        private double byy;
        private double bzz;
        private double wyyPlus;
        private double wyyMinus;
        private double wzzPlus;
        private double wzzMinus;
        private double offsetY;
        private double offsetZ;
        #endregion

        public Section() { }
        public Section(Section section)
        {
            Name = section.Name;
            foreach (var sp in section.StressPoints)
                StressPoints.Add(new ParisPoint2D(sp.X, sp.Y));
            OffsetY = section.OffsetY;
            OffsetZ = section.OffsetZ;
            Yc = section.Yc;
            Zc = section.Zc;
            Area = section.Area;
            Iyy = section.Iyy;
            Izz = section.Izz;
            Iyz = section.Iyz;
            It = section.It;
            Syy = section.Syy;
            Szz = section.Szz;
            Byy = section.Byy;
            Bzz = section.Bzz;
            WyyMinus = section.WyyMinus;
            WyyPlus = section.WzzPlus;
            WzzMinus = section.WzzMinus;
            WzzPlus = section.WzzPlus;
            Perimeter = section.Perimeter;
            foreach (var shape in section.Shapes)
                Shapes.Add(new _BaseShape(shape));
        }

        #region Property
        public int Id { get; set; }
        /// <summary>
        /// Название сечения
        /// </summary>
        public string Name { get => name; set { name = value; OnPropertyChanged(); } }

        /// <summary>
        /// Центр тяжести по оси у
        /// </summary>
        public double Yc { get => yc; set { yc = value; OnPropertyChanged(); } }

        /// <summary>
        /// Центр тяжести по оси z
        /// </summary>
        public double Zc { get => zc; set { zc = value; OnPropertyChanged(); } }

        /// <summary>
        /// Периметр сечения
        /// </summary>
        public double Perimeter { get => perimeter; set { perimeter = value; OnPropertyChanged(); } }

        /// <summary>
        /// Площадь сечения
        /// </summary>
        public double Area { get => area; set { area = value; OnPropertyChanged(); } }

        /// <summary>
        /// Момент инерции относительно оси у
        /// </summary>
        public double Iyy { get => iyy; set { iyy = value; OnPropertyChanged(); } }

        /// <summary>
        /// Момент инерции относительно оси  z
        /// </summary>
        public double Izz { get => izz; set { izz = value; OnPropertyChanged(); } }

        /// <summary>
        /// Полярный момент инерции 
        /// </summary>
        public double Iyz { get => iyz; set { iyz = value; OnPropertyChanged(); } }

        /// <summary>
        /// Момент инерции на кручение
        /// </summary>
        public double It { get => it; set { it = value; OnPropertyChanged(); } }

        /// <summary>
        /// Статический момент относительно
        /// </summary>
        public double Syy { get => syy; set { syy = value; OnPropertyChanged(); } }

        /// <summary>
        /// Статический момент относительно
        /// </summary>
        public double Szz { get => szz; set { szz = value; OnPropertyChanged(); } }

        /// <summary>
        /// Ширина сечения по оси 
        /// </summary>
        public double Byy { get => byy; set { byy = value; OnPropertyChanged(); } }

        /// <summary>
        /// Ширина сечения по оси
        /// </summary>
        public double Bzz { get => bzz; set { bzz = value; OnPropertyChanged(); } }

        /// <summary>
        /// Момент сопротивления
        /// </summary>
        public double WyyPlus { get => wyyPlus; set { wyyPlus = value; OnPropertyChanged(); } }

        /// <summary>
        /// Момент сопротивления
        /// </summary>
        public double WyyMinus { get => wyyMinus; set { wyyMinus = value; OnPropertyChanged(); } }

        /// <summary>
        /// Момент сопротивления
        /// </summary>
        public double WzzPlus { get => wzzPlus; set { wzzPlus = value; OnPropertyChanged(); } }

        /// <summary>
        /// Момент сопротивления
        /// </summary>
        public double WzzMinus { get => wzzMinus; set { wzzMinus = value; OnPropertyChanged(); } }

        /// <summary>
        /// Величина смещения по Y
        /// </summary>
        public double OffsetY { get => offsetY; set { offsetY = value; OnPropertyChanged(); } }

        /// <summary>
        /// Величина смещения по Z
        /// </summary>
        public double OffsetZ { get => offsetZ; set { offsetZ = value; OnPropertyChanged(); } }

        /// <summary>
        /// Контуры в составе сечения
        /// </summary>
        public List<_BaseShape> Shapes { get; set; } = new List<_BaseShape>();

        /// <summary>
        /// Контуры в составе сборного сечения
        /// </summary>
        //public List<_BaseShape> CompositeShapes { get; set; } = new List<_BaseShape>();

        /// <summary>
        /// Контуры в составе сборного сечения для отрисовки, если это надо (для ортотропного моста, прописать условие в расчете сечения)
        /// </summary>
        //public List<_BaseShape> ShapesForVolume { get; set; } = new List<_BaseShape>();


        /// <summary>
        /// Точки для вычисления напряжений в сечении
        /// </summary>
        public List<ParisPoint2D> StressPoints { get; set; } = new List<ParisPoint2D>();

        /// <summary>
        /// коэффициент к учету нагрузки от собстенного веса
        /// </summary>
        public double WeightFactor { get; set; } = 1.0;

        #endregion

        #region Methods
        /// <summary>
        /// Определение геометрических характеристик сечения
        /// </summary>
        public void Solve()
        {
            Solver _solver = new Solver();

            double perimeter;
            SectionDefinition sec = CreateSectionDefinition(out perimeter);

            if (Shapes.Count() > 0)
            {
                _solver.Solve(sec);

                Yc = Math.Round(sec.Output.SectionProperties.cx, 5);
                Zc = Math.Round(sec.Output.SectionProperties.cy, 5);
                Perimeter = Math.Round(perimeter, 5);
                Area = Math.Round(sec.Output.SectionProperties.Area, 5);
                Iyy = Math.Round(sec.Output.SectionProperties.ixx_c, 7);
                Izz = Math.Round(sec.Output.SectionProperties.iyy_c, 7);
                Iyz = Math.Round(sec.Output.SectionProperties.ixy_c, 7);
                It = Math.Round(sec.Output.SectionProperties.j, 7);
                Syy = Math.Round(sec.Output.SectionProperties.qx_sec, 5);
                Szz = Math.Round(sec.Output.SectionProperties.qy_sec, 5);
                Byy = Math.Round(sec.Output.SectionProperties.bx, 5);
                Bzz = Math.Round(sec.Output.SectionProperties.by, 5);
                WyyPlus = Math.Round(sec.Output.SectionProperties.zxx_plus, 5);
                WyyMinus = Math.Round(sec.Output.SectionProperties.zxx_minus, 5);
                WzzPlus = Math.Round(sec.Output.SectionProperties.zyy_plus, 5);
                WzzMinus = Math.Round(sec.Output.SectionProperties.zyy_minus, 5);

                UpdateStressPoints();
            }
        }

        /// <summary>
        /// Формирование определения сечения из форм
        /// </summary>
        private SectionDefinition CreateSectionDefinition(out double perimeter)
        {
            SectionDefinition sec = new SectionDefinition();
            sec.SolutionSettings = new SolutionSettings() { RunPlasticAnalysis = false };

            perimeter = 0;
            SectionMaterial defaultMat = new SectionMaterial("dummy", 1, 1.0, 0.3, 1.0);
            foreach (_BaseShape shape in Shapes)
            {
                var gc = shape.GetCenterOfGrav(shape.Profile);
                double tol = 0.01;
                foreach (var ec in shape.Profile)
                {
                    var _points = ec.Points.Distinct().ToArray();
                    if (ec.RegionType != RegionType.Hole)
                    {
                        for (int i = 1; i < _points.Length; i++)
                            perimeter += (_points[i - 1] - _points[i]).Length();
                        perimeter += (_points[0] - _points[_points.Length - 1]).Length();
                    }
                    SectionMaterial currentMaterial = defaultMat;
                    sec.Contours.Add(new SectionContour(_points.Select(x => new CrossSection.Point2D(Math.Round(x.X + shape.Location.X - gc.X, 3), Math.Round(x.Y + shape.Location.Y - gc.Y, 3), tol)).ToList(), ec.RegionType == RegionType.Hole, currentMaterial));
                }
            }

            if (Shapes.Count() > 0)
            {
                //удалить близкие точки
                foreach (var con in sec.Contours)
                {
                    var pointsToRemove = new List<CrossSection.Point2D>();
                    for (int j = 0; j < con.Points.Count() - 1; j++)
                        if ((new ParisPoint2D(con.Points[j].X, con.Points[j].Y) - new ParisPoint2D(con.Points[j + 1].X, con.Points[j + 1].Y)).Length() < 9e-2)
                            pointsToRemove.Add(con.Points[j + 1]);
                    foreach (var p in pointsToRemove)
                        con.Points.Remove(p);
                }

                //составляем отрезки чтобы проверить их на пересечение
                List<List<Segment2D>> segments = new List<List<Segment2D>>();
                int segInd = 0;
                foreach (var con in sec.Contours)
                {
                    segments.Add(new List<Segment2D>());

                    for (int i = 0; i < con.Points.Count(); i++)
                    {
                        if (i == con.Points.Count() - 1)
                            segments[segInd].Add(new Segment2D(con.Points[i], con.Points[0]));
                        else
                            segments[segInd].Add(new Segment2D(con.Points[i], con.Points[i + 1]));
                    }
                    segInd++;
                }

                List<Segment2D> allSegments = new List<Segment2D>();
                foreach (var seg in segments)
                    allSegments.AddRange(seg);

                List<ParisPoint2D> updatePoints = new List<ParisPoint2D>();
                List<ParisPoint2D> newPoints = new List<ParisPoint2D>();
                List<double> dises = new List<double>();
                foreach (var seg in allSegments)
                    foreach (var _seg in allSegments)
                    {
                        var p1 = new ParisPoint2D(seg.InitialPoint.X, seg.InitialPoint.Y);
                        var p2 = new ParisPoint2D(seg.TerminalPoint.X, seg.TerminalPoint.Y);
                        var p3 = new ParisPoint2D(_seg.InitialPoint.X, _seg.InitialPoint.Y);
                        var p4 = new ParisPoint2D(_seg.TerminalPoint.X, _seg.TerminalPoint.Y);
                        var points = new List<ParisPoint2D>() { p1, p2, p3, p4 };
                        ParisPoint2D updatePoint = new ParisPoint2D();
                        if (Geometry2DHelper.SegmentsCrossed(p1, p2, p3, p4))
                        {
                            List<Point2D> IntersectionPoint = new List<Point2D>();
                            var crossPoint = Geometry2DHelper.SegmentIntersectionPoint(p1, p2, p3, p4);
                            if (crossPoint != null)
                            {
                                double minDis = 1e5;
                                foreach (var p in points)
                                    if ((p - crossPoint).Length() < minDis)
                                    {
                                        minDis = (p - crossPoint).Length();
                                        updatePoint = p;
                                    }
                                updatePoints.Add(updatePoint);
                                dises.Add(minDis);
                                newPoints.Add(crossPoint);
                            }
                        }
                    }

                for (int i = 0; i < updatePoints.Count(); i++)
                {
                    foreach (var con in sec.Contours)
                        for (int j = 0; j < con.Points.Count(); j++)
                            if ((new ParisPoint2D(con.Points[j].X, con.Points[j].Y) - updatePoints[i]).Length() < 1e-4)
                                con.Points[j] = new CrossSection.Point2D(Math.Round(newPoints[i].X, 1), Math.Round(newPoints[i].Y, 1));
                }
            }

            return sec;
        }

        /// <summary>
        /// Обновление точек напряжений
        /// </summary>
        public List<ParisPoint2D> UpdateStressPoints(bool onlyOffsets = false)
        {
            if (!onlyOffsets)
                StressPoints.Clear();
            if (Shapes.Count == 0) return new List<ParisPoint2D>();

            double totalGCX = 0.0, totalGCY = 0.0, totalArea = 0.0;
            double totalIx = 0;
            double totalIy = 0;

            foreach (var shape in Shapes)
            {
                shape.UpdateCenterOfGrav();
                var axismoments = shape.GetAxisMomens();
                totalIx += axismoments[0] - shape.Area * Math.Pow(shape.GC.Y + shape.Location.Y, 2);
                totalIy += axismoments[1] - shape.Area * Math.Pow(shape.GC.X + shape.Location.X, 2);
                totalGCX += (shape.GC.X + shape.Location.X) * shape.Area;
                totalGCY += (shape.GC.Y + shape.Location.Y) * shape.Area;
                totalArea += shape.Area;

            }
            totalGCX /= totalArea;
            totalGCY /= totalArea;
            totalIx -= totalArea * totalGCY * totalGCY;
            totalIy -= totalArea * totalGCX * totalGCX;

            totalIx = Math.Abs(totalIx);
            totalIy = Math.Abs(totalIy);

            ParisPoint2D totalGCMainMaterial = new ParisPoint2D(totalGCX, totalGCY);

            var totalGC = new ParisPoint2D(totalGCX, totalGCY);

            ParisPoint2D[] _op = new ParisPoint2D[4];
            for (int i = 0; i < _op.Length; i++)
                _op[i] = new ParisPoint2D(totalGCMainMaterial.X, totalGCMainMaterial.Y);


            var allPointsAnchers = new List<ParisPoint2D>();
            var allPoints = new List<ParisPoint2D>();
            //проходим по анкерам
            foreach (var shape in Shapes)
            {
                foreach (var prof in shape.Profile.Where(x => x.RegionType == RegionType.Body))
                {
                    foreach (var p in prof.Points)
                    {
                        if (p.IsAnchor)
                            allPointsAnchers.Add(new ParisPoint2D(p.X + shape.Location.X, p.Y + shape.Location.Y));
                        //allPoints.Add(new ParisPoint2D(p.X + shape.Location.X, p.Y + shape.Location.Y));
                    }
                }
                foreach (var prof in shape.Profile.Where(x => x.RegionType != RegionType.Hole))
                {
                    foreach (var p in prof.Points)
                    {
                        allPoints.Add(new ParisPoint2D(p.X + shape.Location.X, p.Y + shape.Location.Y));
                    }
                }
            }
            allPointsAnchers = allPointsAnchers.Distinct().ToList();
            if (allPointsAnchers.Count() == 0)
                allPointsAnchers = allPoints;
            var maxX = allPointsAnchers.Select(x => x.X).Max();
            var maxY = allPointsAnchers.Select(x => x.Y).Max();
            var minX = allPointsAnchers.Select(x => x.X).Min();
            var minY = allPointsAnchers.Select(x => x.Y).Min();
            double tol = Math.Min(Math.Abs(maxX - minX), Math.Abs(maxY - minY)) / 100.0;
            bool findByX = true;
            bool findByY = true;
            //проверяем есть ли единичные точки
            if (allPointsAnchers.Where(x => Math.Abs(x.X - maxX) < tol).Count() == 1 && allPointsAnchers.Where(x => Math.Abs(x.X - minX) < tol).Count() == 1)
                findByX = false;

            if (allPointsAnchers.Where(x => Math.Abs(x.Y - maxY) < tol).Count() == 1 && allPointsAnchers.Where(x => Math.Abs(x.Y - minY) < tol).Count() == 1)
                findByY = false;

            if (!findByX && !findByY)
            {
                _op[0] = allPointsAnchers.First(x => Math.Abs(x.X - minX) < tol);
                _op[2] = allPointsAnchers.First(x => Math.Abs(x.X - maxX) < tol);
                _op[1] = allPointsAnchers.First(x => Math.Abs(x.Y - maxY) < tol);
                _op[3] = allPointsAnchers.First(x => Math.Abs(x.Y - minY) < tol);
            }
            else
            {
                double My = 1000.0;
                double Mz = totalIy / totalIx * My * 0.99;
                double sigma1max = 0;
                double sigma2max = 0;
                double sigma3max = 0;
                double sigma4max = 0;
                foreach (var p in allPointsAnchers)
                {
                    double y = p.X - totalGCMainMaterial.X;
                    double z = p.Y - totalGCMainMaterial.Y;
                    var sigmaz = Mz / totalIy * y;
                    var sigmay = My / totalIx * z;
                    var sigma = Math.Abs(sigmaz) + Math.Abs(sigmay);

                    if (sigmaz > 0 && sigmay > 0 && Math.Abs(sigma) > sigma1max)
                    {
                        sigma1max = sigma;
                        _op[1] = p;
                    }
                    if (sigmaz < 0 && sigmay > 0 && Math.Abs(sigma) > sigma2max)
                    {
                        sigma2max = sigma;
                        _op[0] = p;
                    }
                    if (sigmaz < 0 && sigmay < 0 && Math.Abs(sigma) > sigma3max)
                    {
                        sigma3max = sigma;
                        _op[3] = p;
                    }
                    if (sigmaz > 0 && sigmay < 0 && Math.Abs(sigma) > sigma4max)
                    {
                        sigma4max = sigma;
                        _op[2] = p;
                    }
                }
                //проверка наложения точки с центром тяжести
                if (_op[0] == totalGCMainMaterial)
                    _op[0] = Geometry2DHelper.GetPointByCondition(allPointsAnchers.ToArray(), true, false);
                if (_op[1] == totalGCMainMaterial)
                    _op[1] = Geometry2DHelper.GetPointByCondition(allPointsAnchers.ToArray(), true, true);
                if (_op[3] == totalGCMainMaterial)
                    _op[3] = Geometry2DHelper.GetPointByCondition(allPointsAnchers.ToArray(), false, false);
                if (_op[2] == totalGCMainMaterial)
                    _op[2] = Geometry2DHelper.GetPointByCondition(allPointsAnchers.ToArray(), false, true);
            }

            var gcPoint = new ParisPoint2D(totalGCX, totalGCY);

            
            if (!onlyOffsets)
                StressPoints.AddRange(new ParisPoint2D[] { _op[0], _op[1], _op[2], _op[3] });
            ParisPoint2D[] offsetPoints = new ParisPoint2D[11];
            offsetPoints[0] = new ParisPoint2D(_op[0]);
            offsetPoints[2] = new ParisPoint2D(_op[1]);
            offsetPoints[6] = new ParisPoint2D(_op[3]);
            offsetPoints[8] = new ParisPoint2D(_op[2]);
            offsetPoints[1] = new ParisPoint2D(gcPoint.X, allPoints.Select(x => x.Y).Max());// Math.Max(_op[0].Y, _op[2].Y));
            offsetPoints[3] = new ParisPoint2D(allPoints.Select(x => x.X).Min(), gcPoint.Y);
            offsetPoints[4] = gcPoint;
            offsetPoints[5] = new ParisPoint2D(allPoints.Select(x => x.X).Max(), gcPoint.Y);
            offsetPoints[7] = new ParisPoint2D(gcPoint.X, allPoints.Select(x => x.Y).Min());

            return offsetPoints.ToList();
        }

        /// <summary>
        /// Определение границ сечения
        /// </summary>
        /// <returns>Прямоугольная область</returns>
        public ParisRect Bounds()
        {
            var rect = new ParisRect();
            foreach (var shape in Shapes)
                rect.Union(shape.Bounds());
            return rect;
        }
        public override bool Equals(object obj) => Equals(obj as Section);

        public bool Equals(Section p)
        {
            if (p is null)
                return false;

            if (ReferenceEquals(this, p))
                return true;

            if (GetType() != p.GetType())
                return false;

            return Name == p.Name && Yc == p.Yc && Zc == p.Zc &&
                Area == p.Area && Iyy == p.Iyy && Izz == p.Izz && Iyz == p.Iyz &&
                It == p.It && Syy == p.Syy && Szz == p.Szz;
        }

        public static bool operator ==(Section p1, Section p2)
        {
            if (p1 is null)
            {
                if (p2 is null)
                    return true;
                return false;
            }
            return p1.Equals(p2);
        }

        public static bool operator !=(Section p1, Section p2) => !(p1 == p2);

        #endregion

        #region INotifyPropertyChanged
        [field: NonSerializedAttribute()]
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string prop = "")
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(prop));
        }
        #endregion
    }
}
