using System.Numerics;
using Raylib_cs;
using YesodScript.Engine.Core;

namespace YesodScript.Runtime;

internal readonly struct CollisionTriangle
{
    public readonly Vector3 A;
    public readonly Vector3 B;
    public readonly Vector3 C;
    public readonly Vector3 Normal;
    public readonly Vector3 Min;
    public readonly Vector3 Max;

    public CollisionTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        A = a;
        B = b;
        C = c;

        var cross = Vector3.Cross(b - a, c - a);
        var lengthSquared = cross.LengthSquared();
        Normal = lengthSquared > 1e-12f ? cross / MathF.Sqrt(lengthSquared) : Vector3.Zero;
        Min = Vector3.Min(a, Vector3.Min(b, c));
        Max = Vector3.Max(a, Vector3.Max(b, c));
    }
}

internal class TriangleMeshCollider
{
    private readonly List<CollisionTriangle> triangles = [];

    public int TriangleCount => triangles.Count;

    public unsafe void AddModel(Model model, float scale)
    {
        for (var meshIndex = 0; meshIndex < model.MeshCount; meshIndex++)
        {
            var mesh = model.Meshes[meshIndex];
            var vertices = mesh.VerticesAs<Vector3>();
            Span<ushort> indices = mesh.Indices != null
                ? mesh.IndicesAs<ushort>()
                : Span<ushort>.Empty;

            for (var triangleIndex = 0; triangleIndex < mesh.TriangleCount; triangleIndex++)
            {
                int i0, i1, i2;
                if (indices.Length >= (triangleIndex + 1) * 3)
                {
                    i0 = indices[triangleIndex * 3];
                    i1 = indices[triangleIndex * 3 + 1];
                    i2 = indices[triangleIndex * 3 + 2];
                }
                else
                {
                    i0 = triangleIndex * 3;
                    i1 = i0 + 1;
                    i2 = i0 + 2;
                }

                if (i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
                    continue;

                // Runtime world coordinates use Z as up
                // Raylib model coordinates use Y
                var a = ToWorld(vertices[i0]) * scale;
                var b = ToWorld(vertices[i1]) * scale;
                var c = ToWorld(vertices[i2]) * scale;
                var triangle = new CollisionTriangle(a, b, c);
                if (triangle.Normal != Vector3.Zero)
                    triangles.Add(triangle);
            }
        }
    }

    public bool ResolveCapsule(
        ref Vector3 center,
        float radius,
        float axisHalfLength,
        Vector3 movement,
        List<Vector3> contactNormals)
    {
        var collided = false;
        var axisStart = center - Vector3.UnitZ * axisHalfLength;
        var axisEnd = center + Vector3.UnitZ * axisHalfLength;

        foreach (var triangle in triangles)
        {
            if (axisEnd.X < triangle.Min.X - radius || axisStart.X > triangle.Max.X + radius ||
                axisEnd.Y < triangle.Min.Y - radius || axisStart.Y > triangle.Max.Y + radius ||
                axisEnd.Z < triangle.Min.Z - radius || axisStart.Z > triangle.Max.Z + radius)
                continue;

            FindClosestPoints(axisStart, axisEnd, triangle, out var axisPoint, out var trianglePoint);
            var offset = axisPoint - trianglePoint;
            var distanceSquared = offset.LengthSquared();
            if (distanceSquared >= radius * radius)
                continue;

            var distance = MathF.Sqrt(distanceSquared);
            var normal = distance > 1e-6f ? offset / distance : triangle.Normal;
            if (distance <= 1e-6f)
            {
                var movementDot = Vector3.Dot(normal, movement);
                if (movementDot > 0f || (MathF.Abs(movementDot) <= 1e-6f && normal.Z < 0f))
                    normal = -normal;
            }

            var correction = normal * (radius - distance + 0.001f);
            center += correction;
            axisStart += correction;
            axisEnd += correction;
            AddUniqueNormal(contactNormals, normal);
            collided = true;
        }

        return collided;
    }

    private static Vector3 ToWorld(Vector3 modelPosition)
    {
        return new(modelPosition.X, modelPosition.Z, modelPosition.Y);
    }

    private static void FindClosestPoints(
        Vector3 segmentStart,
        Vector3 segmentEnd,
        CollisionTriangle triangle,
        out Vector3 segmentPoint,
        out Vector3 trianglePoint)
    {
        segmentPoint = segmentStart;
        trianglePoint = ClosestPointOnTriangle(segmentStart, triangle.A, triangle.B, triangle.C);
        var bestDistanceSquared = Vector3.DistanceSquared(segmentPoint, trianglePoint);

        UpdateClosest(
            segmentEnd,
            ClosestPointOnTriangle(segmentEnd, triangle.A, triangle.B, triangle.C),
            ref segmentPoint,
            ref trianglePoint,
            ref bestDistanceSquared);

        UpdateSegmentEdge(segmentStart, segmentEnd, triangle.A, triangle.B,
            ref segmentPoint, ref trianglePoint, ref bestDistanceSquared);
        UpdateSegmentEdge(segmentStart, segmentEnd, triangle.B, triangle.C,
            ref segmentPoint, ref trianglePoint, ref bestDistanceSquared);
        UpdateSegmentEdge(segmentStart, segmentEnd, triangle.C, triangle.A,
            ref segmentPoint, ref trianglePoint, ref bestDistanceSquared);

        var direction = segmentEnd - segmentStart;
        var denominator = Vector3.Dot(triangle.Normal, direction);
        if (MathF.Abs(denominator) <= 1e-8f)
            return;

        var t = Vector3.Dot(triangle.Normal, triangle.A - segmentStart) / denominator;
        if (t < 0f || t > 1f)
            return;

        var intersection = segmentStart + direction * t;
        var closest = ClosestPointOnTriangle(intersection, triangle.A, triangle.B, triangle.C);
        if (Vector3.DistanceSquared(intersection, closest) < 1e-10f)
        {
            segmentPoint = intersection;
            trianglePoint = intersection;
        }
    }

    private static void UpdateSegmentEdge(
        Vector3 segmentStart,
        Vector3 segmentEnd,
        Vector3 edgeStart,
        Vector3 edgeEnd,
        ref Vector3 bestSegmentPoint,
        ref Vector3 bestTrianglePoint,
        ref float bestDistanceSquared)
    {
        ClosestPointsOnSegments(
            segmentStart, segmentEnd, edgeStart, edgeEnd,
            out var segmentPoint, out var edgePoint);
        UpdateClosest(
            segmentPoint, edgePoint,
            ref bestSegmentPoint, ref bestTrianglePoint, ref bestDistanceSquared);
    }

    private static void UpdateClosest(
        Vector3 candidateSegmentPoint,
        Vector3 candidateTrianglePoint,
        ref Vector3 bestSegmentPoint,
        ref Vector3 bestTrianglePoint,
        ref float bestDistanceSquared)
    {
        var distanceSquared = Vector3.DistanceSquared(candidateSegmentPoint, candidateTrianglePoint);
        if (distanceSquared >= bestDistanceSquared)
            return;

        bestDistanceSquared = distanceSquared;
        bestSegmentPoint = candidateSegmentPoint;
        bestTrianglePoint = candidateTrianglePoint;
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = point - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
            return a;

        var bp = point - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
            return b;

        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            return a + ab * (d1 / (d1 - d3));

        var cp = point - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= 0f)
            return c;

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            return a + ac * (d2 / (d2 - d6));

        var va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        var inverseSum = 1f / (va + vb + vc);
        var barycentricB = vb * inverseSum;
        var barycentricC = vc * inverseSum;
        return a + ab * barycentricB + ac * barycentricC;
    }

    private static void ClosestPointsOnSegments(
        Vector3 p1,
        Vector3 q1,
        Vector3 p2,
        Vector3 q2,
        out Vector3 closest1,
        out Vector3 closest2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        var a = Vector3.Dot(d1, d1);
        var e = Vector3.Dot(d2, d2);
        var f = Vector3.Dot(d2, r);
        float s, t;

        if (a <= 1e-8f && e <= 1e-8f)
        {
            closest1 = p1;
            closest2 = p2;
            return;
        }

        if (a <= 1e-8f)
        {
            s = 0f;
            t = Math.Clamp(f / e, 0f, 1f);
        }
        else
        {
            var c = Vector3.Dot(d1, r);
            if (e <= 1e-8f)
            {
                t = 0f;
                s = Math.Clamp(-c / a, 0f, 1f);
            }
            else
            {
                var b = Vector3.Dot(d1, d2);
                var denominator = a * e - b * b;
                s = denominator != 0f
                    ? Math.Clamp((b * f - c * e) / denominator, 0f, 1f)
                    : 0f;
                t = (b * s + f) / e;
                if (t < 0f)
                {
                    t = 0f;
                    s = Math.Clamp(-c / a, 0f, 1f);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Math.Clamp((b - c) / a, 0f, 1f);
                }
            }
        }

        closest1 = p1 + d1 * s;
        closest2 = p2 + d2 * t;
    }

    private static void AddUniqueNormal(List<Vector3> normals, Vector3 normal)
    {
        foreach (var existing in normals)
        {
            if (Vector3.Dot(existing, normal) > 0.99f)
                return;
        }

        normals.Add(normal);
    }
}

internal class CharacterController3D
{
    private readonly TriangleMeshCollider collider;
    private readonly List<Vector3> contactNormals = [];

    public float Radius { get; }
    public float Height { get; }

    public CharacterController3D(TriangleMeshCollider collider, float radius, float height)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius), "Capsule radius must be finite and greater than zero.");
        if (!float.IsFinite(height) || height <= 0f)
            throw new ArgumentOutOfRangeException(nameof(height), "Capsule height must be finite and greater than zero.");

        this.collider = collider;
        Radius = radius;
        Height = MathF.Max(height, radius * 2f);
    }

    public bool Move(ref Vector3 center, Vector3 displacement)
    {
        var axisHalfLength = Height * 0.5f - Radius;
        var stepCountFloat = MathF.Ceiling(displacement.Length() / (Radius * 0.5f));
        if (!float.IsFinite(stepCountFloat) || stepCountFloat > 10000f)
            throw new ArgumentOutOfRangeException(nameof(displacement), "Movement is too large for a single collision step.");

        var stepCount = Math.Max(1, (int)stepCountFloat);
        var step = displacement / stepCount;
        var grounded = false;

        for (var stepIndex = 0; stepIndex < stepCount; stepIndex++)
        {
            center += step;
            contactNormals.Clear();

            for (var pass = 0; pass < 4; pass++)
            {
                if (!collider.ResolveCapsule(ref center, Radius, axisHalfLength, step, contactNormals))
                    break;
            }

            foreach (var normal in contactNormals)
            {
                if (normal.Z > 0.5f)
                    grounded = true;

                var intoSurface = Vector3.Dot(step, normal);
                if (intoSurface < 0f)
                    step -= normal * intoSurface;
            }
        }

        return grounded;
    }
}

internal static class Collision3D
{
    private static TriangleMeshCollider? collider;
    private static readonly Dictionary<(float Radius, float Height), CharacterController3D> controllers = [];

    public static void LoadModel(string modelID, float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0f)
            throw new ArgumentOutOfRangeException(nameof(scale), "Collision model scale must be finite and greater than zero.");

        var nextCollider = new TriangleMeshCollider();
        nextCollider.AddModel(Assets.Instance.GetModel(modelID), scale);
        if (nextCollider.TriangleCount == 0)
            throw new InvalidOperationException($"Model '{modelID}' has no usable collision triangles.");

        collider = nextCollider;
        controllers.Clear();
    }

    public static bool Move(ref Vector3 center, Vector3 velocity, float deltaTime, float radius, float height)
    {
        if (collider == null)
            throw new InvalidOperationException("No collision model is loaded. Run C3D_LOAD before C3D_MOVE.");
        if (!float.IsFinite(deltaTime) || deltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(velocity.Z))
            throw new ArgumentOutOfRangeException(nameof(velocity), "Movement velocity must be finite.");

        var key = (radius, height);
        if (!controllers.TryGetValue(key, out var controller))
        {
            controller = new CharacterController3D(collider, radius, height);
            controllers.Add(key, controller);
        }

        return controller.Move(ref center, velocity * deltaTime);
    }
}

public class C3D_LOAD : GameCommand
{
    [CommandParameter] private string modelID = "";
    [CommandParameter] private float scale = 1f;

    public override void OnEnter()
    {
        Collision3D.LoadModel(modelID, scale);
    }
}

public class C3D_MOVE : GameCommand
{
    [CommandParameter] private string x = "";
    [CommandParameter] private string y = "";
    [CommandParameter] private string z = "";
    [CommandParameter] private float vx = 0f;
    [CommandParameter] private float vy = 0f;
    [CommandParameter] private float vz = 0f;
    [CommandParameter] private string grounded = "";
    [CommandParameter] private float radius = 0.35f;
    [CommandParameter] private float height = 1.8f;

    public override bool OnTick(float deltaTime)
    {
        var xVar = GetNumberVar(x);
        var yVar = GetNumberVar(y);
        var zVar = GetNumberVar(z);
        var groundedVar = GameVM.GetVar(grounded, Instruction)
            ?? throw new InvalidOperationException($"Grounded var '{grounded}' was not found.");
        if (groundedVar.Type != typeof(bool))
            throw new NotSupportedException($"Grounded var '{grounded}' must be bool.");

        var center = new Vector3(ReadNumber(xVar), ReadNumber(yVar), ReadNumber(zVar));
        groundedVar.Value = Collision3D.Move(ref center, new Vector3(vx, vy, vz), deltaTime, radius, height);
        CoordUtil.StoreNumber(GameVM, Instruction, x, center.X);
        CoordUtil.StoreNumber(GameVM, Instruction, y, center.Y);
        CoordUtil.StoreNumber(GameVM, Instruction, z, center.Z);
        return true;
    }

    private Var GetNumberVar(string name)
    {
        var variable = GameVM.GetVar(name, Instruction)
            ?? throw new InvalidOperationException($"Coordinate var '{name}' was not found.");
        if (variable.Type != typeof(int) && variable.Type != typeof(float))
            throw new NotSupportedException($"Coordinate var '{name}' must be int or float.");

        return variable;
    }

    private static float ReadNumber(Var variable)
    {
        return variable.Type == typeof(float) ? (float)variable.Value : (int)variable.Value;
    }
}
