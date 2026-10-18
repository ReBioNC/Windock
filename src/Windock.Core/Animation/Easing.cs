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

    public static double OutQuad(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return 1 - (1 - t) * (1 - t);
    }

    public static double InQuad(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t;
    }
}
