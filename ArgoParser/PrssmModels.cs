using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ArgoParser
{
    /// <summary>
    /// Корневой объект документа PRSSM
    /// </summary>
    public class PrssmDocument
    {
        [JsonPropertyName("SelectedNode")]
        public object SelectedNode { get; set; } = null;

        [JsonPropertyName("BeamsNumber")]
        public int BeamsNumber { get; set; }

        [JsonPropertyName("Beams")]
        public List<PrssmBeam> Beams { get; set; } = new List<PrssmBeam>();

        [JsonPropertyName("R")]
        public double R { get; set; } = 0;

        [JsonPropertyName("Angle")]
        public double Angle { get; set; } = 0;

        [JsonPropertyName("Slant")]
        public double Slant { get; set; } = 0;

        [JsonPropertyName("SelectedSlab")]
        public PrssmSlab SelectedSlab { get; set; } = new PrssmSlab();

        [JsonPropertyName("BracesNumber")]
        public int BracesNumber { get; set; } = 0;

        [JsonPropertyName("Braces")]
        public object Braces { get; set; } = null;

        [JsonPropertyName("BraceMaterial")]
        public object BraceMaterial { get; set; } = null;

        [JsonPropertyName("SelectedBeamSpanType")]
        public PrssmSpanType SelectedBeamSpanType { get; set; } = new PrssmSpanType();

        [JsonPropertyName("Loads")]
        public object Loads { get; set; } = null;

        [JsonPropertyName("SelfWeights")]
        public object SelfWeights { get; set; } = null;

        [JsonPropertyName("PanelLengths")]
        public object PanelLengths { get; set; } = null;
    }

    /// <summary>
    /// Балка в формате PRSSM
    /// </summary>
    public class PrssmBeam
    {
        [JsonPropertyName("Id")]
        public int Id { get; set; }

        [JsonPropertyName("BeamParts")]
        public List<PrssmBeamPart> BeamParts { get; set; } = new List<PrssmBeamPart>();

        [JsonPropertyName("BeamPartsNumber")]
        public int BeamPartsNumber { get; set; }

        [JsonPropertyName("Material")]
        public PrssmMaterial Material { get; set; }

        [JsonPropertyName("Step")]
        public double Step { get; set; }

        [JsonPropertyName("Position")]
        public double Position { get; set; }

        [JsonPropertyName("R")]
        public double R { get; set; } = 0;

        [JsonPropertyName("LongitudinalReinforcementNumber")]
        public int LongitudinalReinforcementNumber { get; set; }

        [JsonPropertyName("TransverseReinforcementNumber")]
        public int TransverseReinforcementNumber { get; set; }

        [JsonPropertyName("ReinforcementLongitudinals")]
        public List<PrssmLongitudinalReinforcement> ReinforcementLongitudinals { get; set; } = new List<PrssmLongitudinalReinforcement>();

        [JsonPropertyName("ReinforcementTransverses")]
        public List<PrssmTransverseReinforcement> ReinforcementTransverses { get; set; } = new List<PrssmTransverseReinforcement>();
    }

    /// <summary>
    /// Участок балки (секция по длине)
    /// </summary>
    public class PrssmBeamPart
    {
        [JsonPropertyName("Id")]
        public int Id { get; set; }

        [JsonPropertyName("Section")]
        public PrssmSection Section { get; set; }

        [JsonPropertyName("Length")]
        public double Length { get; set; }

        [JsonPropertyName("Division")]
        public int Division { get; set; } = 5;

        [JsonPropertyName("IsStartPier")]
        public bool IsStartPier { get; set; } = true;

        [JsonPropertyName("IsEndPier")]
        public bool IsEndPier { get; set; } = true;
    }

    /// <summary>
    /// Поперечное сечение
    /// </summary>
    public class PrssmSection
    {
        [JsonPropertyName("Name")]
        public string Name { get; set; } = "Пользовательское";

        [JsonPropertyName("SectionType")]
        public int SectionType { get; set; } = 1;

        [JsonPropertyName("Yc")]
        public double Yc { get; set; }

        [JsonPropertyName("Zc")]
        public double Zc { get; set; }

        [JsonPropertyName("Perimeter")]
        public double Perimeter { get; set; }

        [JsonPropertyName("Area")]
        public double Area { get; set; }

        [JsonPropertyName("Iyy")]
        public double Iyy { get; set; }

        [JsonPropertyName("Izz")]
        public double Izz { get; set; }

        [JsonPropertyName("Iyz")]
        public double Iyz { get; set; }

        [JsonPropertyName("It")]
        public double It { get; set; }

        [JsonPropertyName("Syy")]
        public double Syy { get; set; }

        [JsonPropertyName("Szz")]
        public double Szz { get; set; }

        [JsonPropertyName("Byy")]
        public double Byy { get; set; }

        [JsonPropertyName("Bzz")]
        public double Bzz { get; set; }

        [JsonPropertyName("WyyPlus")]
        public double WyyPlus { get; set; }

        [JsonPropertyName("WyyMinus")]
        public double WyyMinus { get; set; }

        [JsonPropertyName("WzzPlus")]
        public double WzzPlus { get; set; }

        [JsonPropertyName("WzzMinus")]
        public double WzzMinus { get; set; }

        [JsonPropertyName("OffsetType")]
        public int OffsetType { get; set; } = 0;

        [JsonPropertyName("OffsetY")]
        public double OffsetY { get; set; } = 0;

        [JsonPropertyName("OffsetZ")]
        public double OffsetZ { get; set; } = 0;

        [JsonPropertyName("Shapes")]
        public List<PrssmShape> Shapes { get; set; } = new List<PrssmShape>();

        [JsonPropertyName("StressPoints")]
        public List<PrssmPoint> StressPoints { get; set; } = new List<PrssmPoint>();

        [JsonPropertyName("WeightFactor")]
        public double WeightFactor { get; set; } = 1;

        [JsonPropertyName("Id")]
        public int Id { get; set; }
    }

    /// <summary>
    /// Геометрическая форма (контур сечения)
    /// </summary>
    public class PrssmShape
    {
        [JsonPropertyName("Id")]
        public int Id { get; set; }

        [JsonPropertyName("CadType")]
        public int CadType { get; set; } = 0;

        [JsonPropertyName("Name")]
        public string Name { get; set; } = "custom";

        [JsonPropertyName("Beta")]
        public double Beta { get; set; } = 0;

        [JsonPropertyName("IsReflectX")]
        public bool IsReflectX { get; set; } = false;

        [JsonPropertyName("IsReflectY")]
        public bool IsReflectY { get; set; } = false;

        [JsonPropertyName("Location")]
        public PrssmPoint Location { get; set; } = new PrssmPoint();

        [JsonPropertyName("Links")]
        public List<object> Links { get; set; } = new List<object>();

        [JsonPropertyName("Profile")]
        public List<PrssmProfileRegion> Profile { get; set; } = new List<PrssmProfileRegion>();
    }

    /// <summary>
    /// Регион профиля (замкнутый контур)
    /// </summary>
    public class PrssmProfileRegion
    {
        [JsonPropertyName("RegionType")]
        public int RegionType { get; set; } = 0;

        [JsonPropertyName("Points")]
        public List<PrssmProfilePoint> Points { get; set; } = new List<PrssmProfilePoint>();
    }

    /// <summary>
    /// Точка профиля
    /// </summary>
    public class PrssmProfilePoint
    {
        [JsonPropertyName("X")]
        public double X { get; set; }

        [JsonPropertyName("Y")]
        public double Y { get; set; }

        [JsonPropertyName("IsAnchor")]
        public bool IsAnchor { get; set; } = true;

        [JsonPropertyName("IsProfilePoint")]
        public bool IsProfilePoint { get; set; } = true;
    }

    /// <summary>
    /// Простая точка (X, Y)
    /// </summary>
    public class PrssmPoint
    {
        [JsonPropertyName("X")]
        public double X { get; set; }

        [JsonPropertyName("Y")]
        public double Y { get; set; }

        public PrssmPoint() { }

        public PrssmPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Материал
    /// </summary>
    public class PrssmMaterial
    {
        [JsonPropertyName("Name")]
        public string Name { get; set; }

        [JsonPropertyName("MaterialType")]
        public int MaterialType { get; set; } = 0;

        [JsonPropertyName("StandartMaterialType")]
        public int StandartMaterialType { get; set; }

        [JsonPropertyName("YoungModulus")]
        public double YoungModulus { get; set; }

        [JsonPropertyName("PoissonRatio")]
        public double PoissonRatio { get; set; } = 0.2;

        [JsonPropertyName("SpecificWeight")]
        public double SpecificWeight { get; set; } = 2.45E-05;

        [JsonPropertyName("ThermalCoefficient")]
        public double ThermalCoefficient { get; set; } = 1E-05;

        [JsonPropertyName("Strength")]
        public double Strength { get; set; } = 0;

        [JsonPropertyName("CompressiveStrength")]
        public double CompressiveStrength { get; set; } = 0;

        [JsonPropertyName("FluidityStrength")]
        public double FluidityStrength { get; set; } = 0;

        [JsonPropertyName("TemporaryStrength")]
        public double TemporaryStrength { get; set; } = 0;

        [JsonPropertyName("GammaM")]
        public double GammaM { get; set; } = 1;

        [JsonPropertyName("StandartConcreteType")]
        public int StandartConcreteType { get; set; } = 0;

        [JsonPropertyName("ConcreteYoungModulus")]
        public double ConcreteYoungModulus { get; set; } = 0;

        [JsonPropertyName("ConcreteSpecificWeight")]
        public double ConcreteSpecificWeight { get; set; } = 0;

        [JsonPropertyName("Sigd")]
        public object Sigd { get; set; } = null;

        [JsonPropertyName("Epsd")]
        public object Epsd { get; set; } = null;

        [JsonPropertyName("Rof")]
        public object Rof { get; set; } = null;

        [JsonPropertyName("Sigf")]
        public object Sigf { get; set; } = null;

        [JsonPropertyName("Nf")]
        public object Nf { get; set; } = null;

        [JsonPropertyName("Id")]
        public int Id { get; set; }
    }

    /// <summary>
    /// Продольное армирование
    /// </summary>
    public class PrssmLongitudinalReinforcement
    {
        [JsonPropertyName("Angle")]
        public double Angle { get; set; } = 0;

        [JsonPropertyName("Radius")]
        public double Radius { get; set; } = 0;

        [JsonPropertyName("ReinforcementType")]
        public int ReinforcementType { get; set; } = 0;

        [JsonPropertyName("Name")]
        public string Name { get; set; } = null;

        [JsonPropertyName("Diameter")]
        public double Diameter { get; set; }

        [JsonPropertyName("NAtItem")]
        public int NAtItem { get; set; } = 1;

        [JsonPropertyName("ItemsAtRow")]
        public int ItemsAtRow { get; set; }

        [JsonPropertyName("StepElement")]
        public double StepElement { get; set; }

        [JsonPropertyName("OffsetFromStart")]
        public double OffsetFromStart { get; set; }

        [JsonPropertyName("YOffset")]
        public double YOffset { get; set; }

        [JsonPropertyName("ZOffset")]
        public double ZOffset { get; set; }

        [JsonPropertyName("SegmentCount")]
        public int SegmentCount { get; set; }

        [JsonPropertyName("Segments")]
        public List<PrssmReinforcementSegment> Segments { get; set; } = new List<PrssmReinforcementSegment>();

        [JsonPropertyName("BindingPoint")]
        public PrssmPoint BindingPoint { get; set; }
    }

    /// <summary>
    /// Поперечное армирование (хомуты)
    /// </summary>
    public class PrssmTransverseReinforcement
    {
        [JsonPropertyName("IsClosed")]
        public bool IsClosed { get; set; } = false;

        [JsonPropertyName("Name")]
        public string Name { get; set; } = null;

        [JsonPropertyName("Diameter")]
        public double Diameter { get; set; }

        [JsonPropertyName("NAtItem")]
        public int NAtItem { get; set; } = 1;

        [JsonPropertyName("ItemsAtRow")]
        public int ItemsAtRow { get; set; }

        [JsonPropertyName("StepElement")]
        public double StepElement { get; set; }

        [JsonPropertyName("OffsetFromStart")]
        public double OffsetFromStart { get; set; }

        [JsonPropertyName("YOffset")]
        public double YOffset { get; set; }

        [JsonPropertyName("ZOffset")]
        public double ZOffset { get; set; }

        [JsonPropertyName("SegmentCount")]
        public int SegmentCount { get; set; }

        [JsonPropertyName("Segments")]
        public List<PrssmReinforcementSegment> Segments { get; set; } = new List<PrssmReinforcementSegment>();

        [JsonPropertyName("BindingPoint")]
        public PrssmPoint BindingPoint { get; set; }
    }

    /// <summary>
    /// Сегмент армирования
    /// </summary>
    public class PrssmReinforcementSegment
    {
        [JsonPropertyName("Length")]
        public double Length { get; set; }

        [JsonPropertyName("Angle")]
        public double Angle { get; set; } = 0;

        [JsonPropertyName("Height")]
        public double Height { get; set; } = 0;

        [JsonPropertyName("Radius")]
        public double Radius { get; set; } = 0;
    }

    /// <summary>
    /// Плита
    /// </summary>
    public class PrssmSlab
    {
        [JsonPropertyName("Thickness")]
        public double Thickness { get; set; } = 0;

        [JsonPropertyName("K1")]
        public double K1 { get; set; } = 0;

        [JsonPropertyName("K2")]
        public double K2 { get; set; } = 0;

        [JsonPropertyName("PanelNumber")]
        public int PanelNumber { get; set; } = 0;

        [JsonPropertyName("DeltaX")]
        public double DeltaX { get; set; } = 0;

        [JsonPropertyName("DeltaZ")]
        public double DeltaZ { get; set; } = 0;

        [JsonPropertyName("Width")]
        public string Width { get; set; } = "1000";

        [JsonPropertyName("IsGrouped")]
        public bool IsGrouped { get; set; } = false;

        [JsonPropertyName("Material")]
        public object Material { get; set; } = null;
    }

    /// <summary>
    /// Тип пролётного строения
    /// </summary>
    public class PrssmSpanType
    {
        [JsonPropertyName("Key")]
        public string Key { get; set; } = "StraightSpan";

        [JsonPropertyName("Name")]
        public string Name { get; set; } = "ПС на прямой";
    }
}
