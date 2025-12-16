namespace Task4.Models;

public class WallData
{
    public Curve Curve { get; set; }
    public double Length { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double BaseElevation { get; set; }
    public XYZ Direction { get; set; }
    public XYZ Normal { get; set; }
}