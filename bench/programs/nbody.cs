// n-body — Computer Language Benchmarks Game (simplified port to the csharpc subset).
// Double-precision physics: exercises field access, virtual-free method calls and Math.Sqrt.
class Body
{
    public double X, Y, Z, VX, VY, VZ, Mass;

    public Body(double x, double y, double z, double vx, double vy, double vz, double mass)
    {
        X = x; Y = y; Z = z; VX = vx; VY = vy; VZ = vz; Mass = mass;
    }
}

static class NBody
{
    const double Pi = 3.141592653589793;
    const double SolarMass = 4 * Pi * Pi;
    const double DaysPerYear = 365.24;

    static void Main()
    {
        int steps = 5000000;
        Body[] bodies = CreateBodies();
        OffsetMomentum(bodies);
        Console.WriteLine(Energy(bodies));
        for (int i = 0; i < steps; i++) Advance(bodies, 0.01);
        Console.WriteLine(Energy(bodies));
    }

    static Body[] CreateBodies()
    {
        Body[] bodies = new Body[5];
        bodies[0] = new Body(0, 0, 0, 0, 0, 0, SolarMass);
        bodies[1] = new Body(4.84143144246472090e+00, -1.16032004402742839e+00, -1.03622044471123109e-01,
            1.66007664274403694e-03 * DaysPerYear, 7.69901118419740425e-03 * DaysPerYear, -6.90460016972063023e-05 * DaysPerYear,
            9.54791938424326609e-04 * SolarMass);
        bodies[2] = new Body(8.34336671824457987e+00, 4.12479856412430479e+00, -4.03523417114321381e-01,
            -2.76742510726862411e-03 * DaysPerYear, 4.99852801234917238e-03 * DaysPerYear, 2.30417297573763929e-05 * DaysPerYear,
            2.85885980666130812e-04 * SolarMass);
        bodies[3] = new Body(1.28943695621391310e+01, -1.51111514016986312e+01, -2.23307578892655734e-01,
            2.96460137564761618e-03 * DaysPerYear, 2.37847173959480950e-03 * DaysPerYear, -2.96589568540237556e-05 * DaysPerYear,
            4.36624404335156298e-05 * SolarMass);
        bodies[4] = new Body(1.53796971148509165e+01, -2.59193146099879641e+01, 1.79258772950371181e-01,
            2.68067772490389322e-03 * DaysPerYear, 1.62824170038242295e-03 * DaysPerYear, -9.51592254519715870e-05 * DaysPerYear,
            5.15138902046611451e-05 * SolarMass);
        return bodies;
    }

    static void OffsetMomentum(Body[] bodies)
    {
        double px = 0, py = 0, pz = 0;
        for (int i = 0; i < bodies.Length; i++)
        {
            Body b = bodies[i];
            px += b.VX * b.Mass;
            py += b.VY * b.Mass;
            pz += b.VZ * b.Mass;
        }
        Body sun = bodies[0];
        sun.VX = -px / SolarMass;
        sun.VY = -py / SolarMass;
        sun.VZ = -pz / SolarMass;
    }

    static void Advance(Body[] bodies, double dt)
    {
        int n = bodies.Length;
        for (int i = 0; i < n; i++)
        {
            Body bi = bodies[i];
            for (int j = i + 1; j < n; j++)
            {
                Body bj = bodies[j];
                double dx = bi.X - bj.X;
                double dy = bi.Y - bj.Y;
                double dz = bi.Z - bj.Z;
                double d2 = dx * dx + dy * dy + dz * dz;
                double mag = dt / (d2 * Math.Sqrt(d2));
                bi.VX -= dx * bj.Mass * mag;
                bi.VY -= dy * bj.Mass * mag;
                bi.VZ -= dz * bj.Mass * mag;
                bj.VX += dx * bi.Mass * mag;
                bj.VY += dy * bi.Mass * mag;
                bj.VZ += dz * bi.Mass * mag;
            }
        }
        for (int i = 0; i < n; i++)
        {
            Body b = bodies[i];
            b.X += dt * b.VX;
            b.Y += dt * b.VY;
            b.Z += dt * b.VZ;
        }
    }

    static double Energy(Body[] bodies)
    {
        double e = 0;
        for (int i = 0; i < bodies.Length; i++)
        {
            Body bi = bodies[i];
            e += 0.5 * bi.Mass * (bi.VX * bi.VX + bi.VY * bi.VY + bi.VZ * bi.VZ);
            for (int j = i + 1; j < bodies.Length; j++)
            {
                Body bj = bodies[j];
                double dx = bi.X - bj.X;
                double dy = bi.Y - bj.Y;
                double dz = bi.Z - bj.Z;
                e -= bi.Mass * bj.Mass / Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }
        return e;
    }
}
