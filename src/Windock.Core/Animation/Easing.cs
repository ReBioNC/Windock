namespace Windock.Core.Animation;

public static class Easing
{
    public static double OutCubic(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var u = 1 - t;
        return 1 - u * u * u;
    }

    public static double InCubic(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * t;
    }
}
