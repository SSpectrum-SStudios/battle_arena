#nullable enable

using Godot;

namespace BattleArena.Movement;

public partial class MovementTestCourse : Node3D
{
    private readonly Color _groundColor = new("426b69");
    private readonly Color _laneColor = new("ddb967");
    private readonly Color _obstacleColor = new("d76459");
    private readonly Color _platformColor = new("6684c5");
    private readonly Color _futureColor = new("9b72cf");

    public override void _Ready()
    {
        BuildGround();
        BuildAccelerationLane();
        BuildSlalom();
        BuildStairs();
        BuildSlope();
        BuildNarrowPath();
        BuildGenericLedges();
        BuildFutureMovementArea();
    }

    private void BuildGround()
    {
        AddBox("MainGround", new Vector3(0f, -0.5f, 0f), new Vector3(64f, 1f, 76f), _groundColor);
        AddStationLabel("START / OPEN TURNING", new Vector3(0f, 0.05f, 27f), 0f);
    }

    private void BuildAccelerationLane()
    {
        AddBox(
            "AccelerationLane",
            new Vector3(-20f, 0.012f, 3f),
            new Vector3(5f, 0.024f, 54f),
            _laneColor,
            collisionEnabled: false);
        for (var marker = 0; marker <= 50; marker += 5)
        {
            var z = 28f - marker;
            AddBox(
                $"AccelerationMark{marker}",
                new Vector3(-20f, 0.028f, z),
                new Vector3(4.5f, 0.012f, 0.12f),
                Colors.White,
                collisionEnabled: false);
        }

        AddStationLabel("50 m ACCELERATION / BRAKING", new Vector3(-20f, 0.08f, 30f), 0f);
    }

    private void BuildSlalom()
    {
        AddStationLabel("HIGH-SPEED STEERING", new Vector3(-10f, 0.08f, 20f), 0f);
        for (var index = 0; index < 8; index++)
        {
            var x = -12f + ((index % 2) * 4f);
            var z = 15f - (index * 4f);
            AddCylinder($"Slalom{index}", new Vector3(x, 1f, z), 0.55f, 2f, _obstacleColor);
        }
    }

    private void BuildStairs()
    {
        AddStationLabel("STAIRS", new Vector3(0f, 0.08f, 4f), 0f);
        const int stepCount = 8;
        for (var step = 0; step < stepCount; step++)
        {
            var height = (step + 1) * 0.22f;
            AddBox(
                $"Stair{step}",
                new Vector3(0f, height * 0.5f, 1f - (step * 0.8f)),
                new Vector3(3.5f, height, 0.8f),
                _platformColor,
                collisionEnabled: false);
        }

        // Fast character controllers should traverse an authored stair flight as
        // a continuous floor. The visible steps remain representative while the
        // hidden ramp removes edge-by-edge collision ambiguity.
        AddCollisionBox(
            "StairTraversalRamp",
            new Vector3(0f, 0.76f, -1.8f),
            new Vector3(3.45f, 0.24f, 6.65f),
            new Vector3(15.4f, 0f, 0f));

        AddBox("StairTop", new Vector3(0f, 1.585f, -8.5f), new Vector3(5f, 0.35f, 7f), _platformColor);
        AddBox("StairRampDown", new Vector3(0f, 0.75f, -15.5f), new Vector3(5f, 0.35f, 7f), _platformColor, new Vector3(-14f, 0f, 0f));
    }

    private void BuildSlope()
    {
        AddStationLabel("SLOPE CONTROL", new Vector3(10f, 0.08f, 10f), 0f);
        AddWedgeRamp(
            "SlopeUp",
            new Vector3(10f, 0f, 3f),
            width: 5f,
            length: 11f,
            rise: 2.45f,
            yawDegrees: 0f,
            color: _platformColor);
        AddBox(
            "SlopeEntranceBand",
            new Vector3(10f, 0.015f, 8.38f),
            new Vector3(5f, 0.03f, 0.24f),
            _laneColor,
            collisionEnabled: false);
        AddBox("SlopePlatform", new Vector3(10f, 2.25f, -3.7f), new Vector3(5f, 0.4f, 3f), _platformColor);
        AddWedgeRamp(
            "CrossSlope",
            new Vector3(15f, 0f, -3.7f),
            width: 3f,
            length: 5f,
            rise: 2.45f,
            yawDegrees: 90f,
            color: _platformColor);
        AddBox(
            "CrossSlopeEntranceBand",
            new Vector3(17.38f, 0.015f, -3.7f),
            new Vector3(0.24f, 0.03f, 3f),
            _laneColor,
            collisionEnabled: false);
    }

    private void BuildNarrowPath()
    {
        AddStationLabel("NARROW PATH", new Vector3(20f, 0.08f, 16f), 0f);
        AddBox("NarrowPath", new Vector3(20f, 0.06f, 2f), new Vector3(1.4f, 0.12f, 25f), _laneColor);
        AddBox("NarrowLanding", new Vector3(20f, 0.08f, -12f), new Vector3(5f, 0.16f, 4f), _laneColor);
    }

    private void BuildFutureMovementArea()
    {
        AddStationLabel("CROUCH / ROLL CLEARANCE", new Vector3(9f, 0.08f, 27f), 0f);
        AddBox("LowTunnelLeft", new Vector3(7f, 1.55f, 22f), new Vector3(3.5f, 0.35f, 6f), _futureColor);
        AddBox("LowTunnelRight", new Vector3(11f, 1.2f, 22f), new Vector3(3.5f, 0.35f, 6f), _futureColor);
        AddBox("RollGapTop", new Vector3(15f, 0.95f, 22f), new Vector3(3.5f, 0.35f, 6f), _futureColor);
    }

    private void BuildGenericLedges()
    {
        AddStationLabel("OMNIDIRECTIONAL LEDGES", new Vector3(-3f, 0.08f, 36f), 0f);
        AddBox("Ledge10cm", new Vector3(-6f, 0.05f, 32f), new Vector3(2.5f, 0.1f, 2.5f), _obstacleColor);
        AddBox("Ledge20cm", new Vector3(-3f, 0.1f, 32f), new Vector3(2.5f, 0.2f, 2.5f), _obstacleColor);
        AddBox("Ledge35cm", new Vector3(0f, 0.175f, 32f), new Vector3(2.5f, 0.35f, 2.5f), _obstacleColor);
    }

    private void AddBox(
        string nodeName,
        Vector3 position,
        Vector3 size,
        Color color,
        Vector3? rotationDegrees = null,
        bool collisionEnabled = true)
    {
        var body = new StaticBody3D
        {
            Name = nodeName,
            Position = position,
            RotationDegrees = rotationDegrees ?? Vector3.Zero,
        };
        if (collisionEnabled)
        {
            var shape = new BoxShape3D { Size = size };
            body.AddChild(new CollisionShape3D { Shape = shape });
        }
        body.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = CreateMaterial(color),
        });
        AddChild(body);
    }

    private void AddCylinder(string nodeName, Vector3 position, float radius, float height, Color color)
    {
        var body = new StaticBody3D { Name = nodeName, Position = position };
        body.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = radius, Height = height },
        });
        body.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = height,
            },
            MaterialOverride = CreateMaterial(color),
        });
        AddChild(body);
    }

    private void AddCollisionBox(string nodeName, Vector3 position, Vector3 size, Vector3 rotationDegrees)
    {
        var body = new StaticBody3D
        {
            Name = nodeName,
            Position = position,
            RotationDegrees = rotationDegrees,
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = size },
        });
        AddChild(body);
    }

    private void AddWedgeRamp(
        string nodeName,
        Vector3 position,
        float width,
        float length,
        float rise,
        float yawDegrees,
        Color color)
    {
        const float buriedDepth = 0.12f;
        var halfWidth = width * 0.5f;
        var halfLength = length * 0.5f;
        var vertices = new[]
        {
            new Vector3(-halfWidth, -buriedDepth, halfLength),
            new Vector3(halfWidth, -buriedDepth, halfLength),
            new Vector3(-halfWidth, 0f, halfLength),
            new Vector3(halfWidth, 0f, halfLength),
            new Vector3(-halfWidth, -buriedDepth, -halfLength),
            new Vector3(halfWidth, -buriedDepth, -halfLength),
            new Vector3(-halfWidth, rise, -halfLength),
            new Vector3(halfWidth, rise, -halfLength),
        };
        var triangles = new[]
        {
            0, 4, 5, 0, 5, 1,
            0, 1, 3, 0, 3, 2,
            4, 6, 7, 4, 7, 5,
            0, 2, 6, 0, 6, 4,
            1, 5, 7, 1, 7, 3,
            2, 3, 7, 2, 7, 6,
        };

        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var vertexIndex in triangles)
        {
            surface.AddVertex(vertices[vertexIndex]);
        }

        surface.GenerateNormals();
        var mesh = surface.Commit();
        var body = new StaticBody3D
        {
            Name = nodeName,
            Position = position,
            RotationDegrees = new Vector3(0f, yawDegrees, 0f),
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new ConvexPolygonShape3D { Points = vertices },
        });
        var rampMaterial = CreateMaterial(color);
        // The convex collider is unaffected by render winding. The procedural
        // mesh uses explicit outward normals and renders both sides so importer
        // or backend front-face conventions cannot make the traversal surface
        // disappear.
        rampMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        body.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = rampMaterial,
        });
        AddChild(body);
    }

    private void AddStationLabel(string text, Vector3 position, float yawDegrees)
    {
        AddChild(new Label3D
        {
            Text = text,
            Position = position + new Vector3(0f, 0.04f, 0f),
            RotationDegrees = new Vector3(-90f, yawDegrees, 0f),
            FontSize = 42,
            OutlineSize = 8,
            Modulate = Colors.White,
            NoDepthTest = false,
        });
    }

    private static StandardMaterial3D CreateMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Roughness = 0.82f,
    };
}
