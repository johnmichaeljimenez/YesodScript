using FluentAssertions;
using YesodScript.Engine.Commands;
using YesodScript.Engine.Core;

namespace YesodScript.Engine.Tests;

public partial class EngineTests
{
    [Fact]
    public void Test_Dump()
    {
        var commands = new string[] { "WAIT", "MUL", "JUMPIF" };
        var vmOutput = "";
        var vm = new VirtualMachine();

        vm.OnOutput += (str) => { vmOutput += $"{str}"; };
        vm.DumpCommands(commands);
        Console.WriteLine(vmOutput);

        vmOutput.Should().ContainAll(commands);
    }
    [Fact]
    public void Test_Directory()
    {
        var vm = new VirtualMachine();
        vm.CompileDirectory("scripts/special", "ysl");
        vm.FindInstruction("Minsky").Should().NotBeNull();
    }

    [Fact]
    public void Test_ErrorCompiler()
    {
        FluentActions.Invoking(() => Utils.Test(out var output, "Error-Compile.ysl"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Compiler error at 'Error-Compile' line 1: Unknown command: 'INVALID'");
    }

    [Fact]
    public void Test_ErrorRuntime()
    {
        FluentActions.Invoking(() => Utils.Test(out var output, "Error-Runtime.ysl"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Runtime error at 'Error-Runtime' line 4: The input string 'hello' was not in a correct format.");
    }

    [Fact]
    public void Test_Regex()
    {
        var strSplitArg = """CALL "HELLO WORLD HE SAYS "HI"" 10""";
        Core.Utils.SplitArguments(strSplitArg).Should().BeEquivalentTo("CALL", "HELLO WORLD HE SAYS HI", "10");
    }

    [Fact]
    public void Test_HelloWorld()
    {
        var vm = Utils.Test(out var output, "HelloWorld.ysl");
        output.Should().Be("HELLO WORLD");
    }

    [Fact]
    public void Test_Trigger()
    {
        var vm = Utils.Test(out var output, "Trigger1.ysl", "Trigger2.ysl");
        output.Should().Be("TRIGGER TEST\nHELLO I AM FROM TRIGGER2, MY NUMBER IS 120\nMY RESPONSE IS: GOODBYE\nFOLLOWUP: WHY?\nTrue");
    }

    [Fact]
    public void Test_CallShorthand()
    {
        var vm = Utils.Test(out var output, "CallShorthand.ysl", "CallShorthand2.ysl");
        output.Should().Be("HELLO I AM FROM SHORTHAND, MY NUMBER IS 120\nMY RESPONSE IS: GOOD BYE\nSAID: HELLO WORLD");
    }

    [Fact]
    public void Test_Concat()
    {
        var vm = Utils.Test(out var output, "Concat.ysl");
        output.Should().Be("My gold is: 30\nI will pay 8\nMy gold is: 22");
    }

    [Fact]
    public void Test_Math()
    {
        var vm = Utils.Test(out var output, "Math.ysl");
        output.Should().Be("15\n12\n48\n9");
    }

    [Fact]
    public void Test_Vector2()
    {
        var vm = Utils.Test(out var output, "Vector2.ysl");
        output.Should().Be("3,4\n1,2\n2,2\n4,5\n3,4\n5\n25\n4\n0.6,0.8\n5,7.5\n0\n0.6\n0.8\nHIT\nOK");
    }

    [Fact]
    public void Test_Params()
    {
        var vm = Utils.Test(out var output, "Params.ysl", "Params2.ysl", "Params3.ysl");
        output.Should().Be("CALLED PARAMS 3\nTHE SUM IS: 12");
    }

    [Fact]
    public void Test_ExecOrder()
    {
        var vm = Utils.Test(out var output, "ExecutionOrder.ysl", "CallShorthand2.ysl", "Params3.ysl");
        var tickCount = vm.CurrentTick;
        tickCount.Should().Be(1);
    }

    [Fact]
    public void Test_RNG()
    {
        var vm = Utils.Test(out var output);
        vm.RNG.Should().NotBeNull();

        vm.RandomSeed = 12;
        vm.RandomSeed.Should().Be(12);
    }

    [Fact]
    public void Test_Lists()
    {
        var vm = Utils.Test(out var output, "Lists.ysl");
        output.Should().Be("apple\nbanana\ncherry\n5\n10\n25\nSUM: 40\n1\n-1\n2");
    }

    [Fact]
    public void Test_DefaultArg()
    {
        var vm = Utils.Test(out var output, "DefaultArg.ysl");
        output.Should().Be("<empty>\n*\n5\n9");
    }

    [Fact]
    public void Test_TriggerSequence()
    {
        var vm = new VirtualMachine();
        vm.CompileFile("scripts/Sequence.ysl");
        vm.Initialize();

        var output = "";
        vm.OnOutput += str => output += $"{str}\n";

        var seq = new TriggerSequence(vm);
        seq.Enqueue("SECOND");
        seq.Enqueue("FIRST");
        seq.Enqueue("   ");//should be ignored
        seq.Enqueue(null);//should be ignored

        while (true)
        {
            seq.Tick();
            vm.Tick(0.033333f);
            if (seq.Tick() && !vm.IsRunning)
                break;
        }

        output.Trim().Should().Be("B1\nA1\nA2");
    }

    [Fact]
    public void Test_TriggerSequence_TickContract()
    {
        var vm = new VirtualMachine();
        vm.CompileFile("scripts/Sequence.ysl");
        vm.Initialize();

        var output = "";
        vm.OnOutput += str => output += $"{str}\n";

        var seq = new TriggerSequence(vm);
        seq.Tick().Should().BeTrue();//empty queue -> done immediately

        seq.Enqueue("FIRST");
        seq.Tick().Should().BeFalse();//something fired and is running

        while (!seq.Tick())
            vm.Tick(0.033333f);

        output.Should().Contain("A1\nA2");
        seq.Tick().Should().BeTrue();//drained
    }

    [Fact]
    public void Test_FireAndForget()
    {
        var vm = Utils.Test(out var output, "FireAndForget.ysl", "Slow.ysl");
        output.Should().Be("START\nAFTER CALL\nSLOW DONE\nMAIN DONE");
    }

    [Fact]
    public void Test_Wait()
    {
        AssertWaitDuration("Wait.ysl");
    }

    [Fact]
    public void Test_Wait_UnderSyncCall()
    {
        AssertWaitDuration("WaitNested.ysl", "WaitNestedChild.ysl");
    }

    private static void AssertWaitDuration(float waitSeconds = 0.25f, float toleranceSeconds = 0.05f, params string[] scriptFiles)
    {
        var vm = new VirtualMachine();
        foreach (var scriptFile in scriptFiles)
            vm.CompileFile($"scripts/{scriptFile}");

        var output = "";
        vm.OnOutput += str => output += $"{str}\n";
        vm.Initialize();

        const float fixedDt = 1f / 60f;
        var ticks = 0;
        var elapsed = 0f;

        while (vm.IsRunning)
        {
            vm.Tick(fixedDt);
            elapsed += fixedDt;
            ticks++;
            if (ticks > 10_000)
                throw new InvalidOperationException("Wait test ran too long.");
        }

        output.Trim().Should().Be("DONE");
        elapsed.Should().BeApproximately(waitSeconds, toleranceSeconds);
    }

    private static void AssertWaitDuration(params string[] scriptFiles)
        => AssertWaitDuration(0.25f, 0.05f, scriptFiles);

    [Fact]
    public void Test_Clone()
    {
        var vm = Utils.Test(out var output, "CloneTest.ysl", "CloneMe.ysl");
        output.Should().Contain("SPAWNED");
        output.Should().Contain("SECRET 99");
        output.Should().Contain("GOT CloneMe#1");

        output.Split("SPAWNED").Length.Should().Be(3);
    }

    [Fact]
    public void Overflow_StaticCallCycle_IsRejectedOnInitialize()
    {
        FluentActions.Invoking(() => Utils.Test(out _, "Overflow1.ysl", "Overflow2.ysl", "Overflow3.ysl"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Sync CALL cycle detected*");
    }

    [Fact]
    public void Overflow_RuntimeCallDepth_IsGuarded()
    {
        var vm = new VirtualMachine
        {
            ValidateCallGraphOnInitialize = false,
            MaxCallDepth = 8
        };

        const int chain = 20;
        for (var i = 0; i < chain; i++)
        {
            var body = i < chain - 1
                ? $"@D{i}\n    $D{i + 1}\n"
                : $"@D{i}\n    PRINT \"END\"\n";
            vm.Compile($"Depth{i}", body);
        }

        vm.Compile("Boot", """
            @_INIT
                $D0
            """);

        FluentActions.Invoking(() => vm.Initialize())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Sync CALL depth exceeded*");
    }

    [Fact]
    public void Overflow_SelfCallCycle_IsRejectedOnInitialize()
    {
        var vm = new VirtualMachine();
        vm.Compile("SelfLoop", """
            @LOOP
                $LOOP
            """);
        vm.Compile("Boot", """
            @_INIT
                $LOOP
            """);

        FluentActions.Invoking(() => vm.Initialize())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Sync CALL cycle detected*LOOP*");
    }

    [Fact]
    public void Overflow_FireAndForget_DoesNotCountAsSyncCycle()
    {
        var vm = new VirtualMachine();
        vm.Compile("Ping", """
            @PING
                PRINT "PING"
                CALL "PONG" * true
            """);
        vm.Compile("Pong", """
            @PONG
                PRINT "PONG"
                CALL "PING" * true
            """);
        vm.Compile("Main", """
            @_INIT
                CALL "PING" * true
                PRINT "OK"
            """);

        vm.OnOutput += _ => { };
        FluentActions.Invoking(() => vm.Initialize()).Should().NotThrow();
    }

    [Fact]
    public void Overflow_SameFileReentrancy_ViaIntermediate_ThrowsCatchableError()
    {
        var vm = new VirtualMachine();
        vm.Compile("ActionInfos", """
            @ACTION:DEFEND
                $BUFFS:ADD
                BREAK

            @BUFF:CAST:DEFEND
                PRINT "CAST"
                BREAK
            """);
        vm.Compile("BuffCast", """
            @BUFFS:ADD
                $BUFF:CAST:DEFEND
                BREAK
            """);
        vm.Compile("Boot", """
            @_INIT
                $ACTION:DEFEND
            """);

        FluentActions.Invoking(() => vm.Initialize())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Re-entrant trigger*BUFF:CAST:DEFEND*ActionInfos*");
    }
}
