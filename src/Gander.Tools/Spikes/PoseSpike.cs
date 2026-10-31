using System.Diagnostics;
using Gander.Core;
using Gander.Core.Pose;
using Gander.Platform.Windows.Pose;

namespace Gander.Tools.Spikes;

/// <summary>
/// Spike S1: can head pose from the webcam tell the monitors apart? Live readout of yaw, pitch and roll, and a guided
/// gate: look at each monitor in turn, twice, and the program says whether they separate clearly. Frames are processed
/// in memory and dropped; only numbers are saved.
/// </summary>
internal static class PoseSpike
{
    private const int Rounds = 2;
    private const int CountdownSeconds = 3;
    private const double SettleSeconds = 1.5;
    private const double HoldSeconds = 3.0;
    private const double MinimumFaceRate = 0.7;
    private const double WarmupSeconds = 2.0;
    private const double AwaySeconds = 2.0;
    private const double ColdHoldSeconds = 3.0;

    /// <summary>Runs the sub-menu (a live readout, b gate run, c describe model, d cold-start test).</summary>
    internal static void Run(string? option)
    {
        if (option is null)
        {
            Console.WriteLine("Pose test:");
            Console.WriteLine("  a  Live readout of yaw, pitch and roll");
            Console.WriteLine("  b  Gate run: look at each monitor and see if they separate");
            Console.WriteLine("  c  Describe the face model (no camera)");
            Console.WriteLine("  d  Cold-start test: how fast is a far-turned face found from no face");
            Console.Write("Choose: ");
            option = Console.ReadLine()?.Trim().ToLowerInvariant();
        }

        switch (option)
        {
            case "a" or "live":
                Live();
                break;
            case "b" or "gate":
                Gate();
                break;
            case "d" or "cold" or "cold-start":
                ColdStart();
                break;
            case "c" or "describe" or "--describe-model":
                Describe();
                break;
        }
    }

    private static void Describe()
    {
        using var report = new Report("gander-pose-model.txt");
        report.Line($"GanderTools face model description, {DateTime.Now:yyyy-MM-dd HH:mm}");
        try
        {
            report.Line(LandmarkModel.Describe(LandmarkModel.DefaultPath, out _));
        }
        catch (Exception exception)
        {
            report.Line($"Could not read the model: {exception.Message}");
        }

        report.Line();
        report.Line($"Also saved to: {report.Path}");
    }

    // ---- a) live readout ----

    private static void Live()
    {
        using var report = new Report("gander-pose-live.txt");
        using var session = Open(report, out var failure);
        if (session is null)
        {
            report.Line(failure!);
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Live readout. Yaw is positive toward your right, pitch positive looking up, roll positive right ear to");
        Console.WriteLine("right shoulder. Press Enter to stop.");
        Console.WriteLine();

        var meters = new Meters();
        long sequence = 0;
        var nextPrint = 0.0;
        var clock = Stopwatch.StartNew();
        PoseFrame? last = null;
        while (!StopRequested())
        {
            using var frame = session.Camera.TryTake(ref sequence);
            if (frame is null)
            {
                Thread.Sleep(5);
                continue;
            }

            last = session.Pipeline.Process(frame);
            meters.Add(last);
            if (clock.Elapsed.TotalSeconds >= nextPrint)
            {
                nextPrint = clock.Elapsed.TotalSeconds + 0.2;
                var pose = last.Pose;
                var text = pose is { } p
                    ? $"yaw {p.Yaw,6:0.0}  pitch {p.Pitch,6:0.0}  roll {p.Roll,6:0.0}  face yes"
                    : "yaw    --    pitch    --    roll    --    face NO ";
                Console.Write($"\r{text}  {meters.Fps,4:0.0} fps  {last.Milliseconds,5:0.0} ms  CPU {meters.CpuPercent,4:0.0}%  {last.Source,-8} lost-run max {meters.Find.LongestLostRun}   ");
            }
        }

        Console.WriteLine();
        report.Line($"Stopped. {meters.Frames} frames, face found in {meters.FaceRate:P0}.");
        report.Line($"Finding: {meters.Find.Summary()}");
    }

    // ---- b) gate run ----

    private static void Gate()
    {
        using var report = new Report("gander-pose.txt");
        report.Line($"GanderTools pose test, {DateTime.Now:yyyy-MM-dd HH:mm}");
        var monitors = Survey.Monitors();
        if (monitors.Count < 2)
        {
            report.Line("This test needs at least two monitors. Plug them in and run it again.");
            return;
        }

        report.Line($"Monitors, left to right: {string.Join("; ", monitors.Select((m, i) => $"monitor {i + 1} {m.Bounds.Width}x{m.Bounds.Height}{(m.Primary ? " (primary)" : string.Empty)}"))}");
        using var session = Open(report, out var failure);
        if (session is null)
        {
            report.Line(failure!);
            return;
        }

        report.Line();
        report.Line($"You will look at each monitor in turn, {Rounds} rounds. Each look: a countdown, a beep, {SettleSeconds:0.#} s to settle,");
        report.Line($"then {HoldSeconds:0.#} s recorded, then a second beep. Look at the monitor the way you naturally do to read it,");
        report.Line("and keep your head still-ish. Sit where you normally sit.");

        var samples = new List<PoseSample>();
        var meters = new Meters();
        var origin = Stopwatch.StartNew();
        Warmup(session);
        for (var round = 1; round <= Rounds; round++)
        {
            for (var index = 0; index < monitors.Count; index++)
            {
                var label = $"monitor {index + 1}";
                report.Line();
                report.Line($"--- Round {round} of {Rounds}: look at monitor {index + 1} ({Position(index, monitors.Count)}) ---");
                Countdown(index + 1, Position(index, monitors.Count));
                Record(session, label, round, origin, samples, meters);
            }
        }

        Finish(report, session, samples, meters);
    }

    /// <summary>Cold-start prompt: look at the screen center for a moment so the tracker starts normally. Not recorded.</summary>
    private static void Warmup(PoseSession session)
    {
        Console.WriteLine();
        Console.WriteLine($"First, look at the CENTER of your screen for {WarmupSeconds:0.#} seconds so tracking can start. Starting in");
        for (var second = CountdownSeconds; second > 0; second--)
        {
            Console.Write($"  {second} ");
            Thread.Sleep(1000);
        }

        Console.WriteLine();
        Console.Beep(660, 150);
        var phase = Stopwatch.StartNew();
        long sequence = 0;
        while (phase.Elapsed.TotalSeconds < WarmupSeconds)
        {
            using var frame = session.Camera.TryTake(ref sequence);
            if (frame is null)
            {
                Thread.Sleep(2);
                continue;
            }

            session.Pipeline.Process(frame);
        }

        Console.Beep(440, 150);
    }

    // ---- d) cold-start test ----

    private static void ColdStart()
    {
        using var report = new Report("gander-pose-coldstart.txt");
        report.Line($"GanderTools cold-start test, {DateTime.Now:yyyy-MM-dd HH:mm}");
        var monitors = Survey.Monitors();
        if (monitors.Count < 2)
        {
            report.Line("This test needs at least two monitors. Plug them in and run it again.");
            return;
        }

        using var session = Open(report, out var failure);
        if (session is null)
        {
            report.Line(failure!);
            return;
        }

        report.Line();
        report.Line($"For each monitor: look AWAY from the camera for {AwaySeconds:0.#} s (at your keyboard or a wall), a beep, then look at");
        report.Line($"the named monitor and hold for {ColdHoldSeconds:0.#} s. We time how long after the beep your face is found.");

        var all = new FindStats();
        var results = new List<string>();
        for (var index = 0; index < monitors.Count; index++)
        {
            var position = Position(index, monitors.Count);
            report.Line();
            report.Line($"--- Cold start: monitor {index + 1} ({position}) ---");
            Console.WriteLine($"Get ready. You will look AWAY from the camera, then at monitor {index + 1} ({position}) after the beep. Starting in");
            for (var second = CountdownSeconds; second > 0; second--)
            {
                Console.Write($"  {second} ");
                Thread.Sleep(1000);
            }

            Console.WriteLine();
            results.Add(ColdStartOne(session, report, index + 1, position, all));
        }

        report.Line();
        report.Line("== Cold-start summary ==");
        foreach (var line in results)
        {
            report.Line(line);
        }

        report.Line($"Finding overall: {all.Summary()}");
        report.Line("Judge by 'seconds after the beep until face found' per monitor: under about 0.5 s is good, never found is a failure.");
        report.Line($"Also saved to: {report.Path}");
    }

    private static string ColdStartOne(PoseSession session, Report report, int number, string position, FindStats all)
    {
        session.Pipeline.Reset();
        Console.WriteLine("  look AWAY from the camera now...");
        var away = Stopwatch.StartNew();
        long sequence = 0;
        var stillSeen = 0;
        while (away.Elapsed.TotalSeconds < AwaySeconds)
        {
            using var frame = session.Camera.TryTake(ref sequence);
            if (frame is null)
            {
                Thread.Sleep(2);
                continue;
            }

            var result = session.Pipeline.Process(frame);
            if (away.Elapsed.TotalSeconds > AwaySeconds - 0.5 && result.Pose is not null)
            {
                stillSeen++;
            }
        }

        session.Pipeline.Reset(); // start from no face whatever happened while looking away
        Console.Beep(880, 150);
        Console.WriteLine($"  NOW look at monitor {number} ({position}) and hold.");
        var hold = Stopwatch.StartNew();
        var stats = new FindStats();
        double? firstFound = null;
        var firstSource = FaceSource.NotFound;
        while (hold.Elapsed.TotalSeconds < ColdHoldSeconds)
        {
            using var frame = session.Camera.TryTake(ref sequence);
            if (frame is null)
            {
                Thread.Sleep(2);
                continue;
            }

            var result = session.Pipeline.Process(frame);
            stats.Add(result.Source);
            all.Add(result.Source);
            if (firstFound is null && result.Pose is not null)
            {
                firstFound = hold.Elapsed.TotalSeconds;
                firstSource = result.Source;
            }
        }

        Console.Beep(440, 200);
        var outcome = firstFound is { } t
            ? $"face found {t:0.00} s after the beep (via {firstSource})"
            : $"face NEVER found in {ColdHoldSeconds:0.#} s";
        var line = $"monitor {number} ({position}): {outcome}; {stats.Summary()}";
        report.Line(line);
        if (stillSeen > 0)
        {
            report.Line($"  note: a face was still being found in the last 0.5 s of looking away ({stillSeen} frames), so this was not a clean cold start.");
        }

        return line;
    }

    private static void Countdown(int number, string position)
    {
        Console.WriteLine($"Look at monitor {number} ({position}) and keep your head still-ish, starting in");
        for (var second = CountdownSeconds; second > 0; second--)
        {
            Console.Write($"  {second} ");
            Thread.Sleep(1000);
        }

        Console.WriteLine();
    }

    /// <summary>Settle (not recorded), then hold (recorded at the camera's frame rate), with a beep at each end.</summary>
    private static void Record(PoseSession session, string label, int round, Stopwatch origin, List<PoseSample> samples, Meters meters)
    {
        Console.Beep(880, 150);
        var phase = Stopwatch.StartNew();
        long sequence = 0;
        var recorded = 0;
        while (phase.Elapsed.TotalSeconds < SettleSeconds + HoldSeconds)
        {
            using var frame = session.Camera.TryTake(ref sequence);
            if (frame is null)
            {
                Thread.Sleep(2);
                continue;
            }

            var result = session.Pipeline.Process(frame);
            if (phase.Elapsed.TotalSeconds < SettleSeconds)
            {
                continue;
            }

            meters.Add(result);
            recorded++;
            samples.Add(result.Pose is { } pose
                ? new PoseSample(origin.Elapsed.TotalSeconds, pose.Yaw, pose.Pitch, pose.Roll, label, round)
                : new PoseSample(origin.Elapsed.TotalSeconds, 0, 0, 0, label, round, Face: false));
        }

        Console.Beep(440, 200);
        Console.WriteLine($"  recorded {recorded} frames.");
    }

    private static void Finish(Report report, PoseSession session, List<PoseSample> samples, Meters meters)
    {
        var tracePath = Path.Combine(AppContext.BaseDirectory, "gander-pose-trace.json");
        File.WriteAllText(tracePath, new PoseTrace(samples).ToJson()); // numbers and labels only

        var result = SeparabilityAnalyzer.Analyze(samples);
        report.Line();
        report.Line("== Result ==");
        report.Line($"Verdict: the monitors {result.VerdictText}.");
        report.Line($"Held-out accuracy (train on one round, test on the other): {(result.HeldOutAccuracy is { } a ? a.ToString("P1") : "n/a")}");
        report.Line();
        report.Line("Per monitor (degrees; yaw + is toward your right, pitch + is up):");
        foreach (var stats in result.Labels)
        {
            report.Line($"  {stats.Label}: {stats.Count} samples, yaw mean {stats.MeanYaw:0.0} (std {stats.StdYaw:0.0}), pitch mean {stats.MeanPitch:0.0} (std {stats.StdPitch:0.0})");
        }

        report.Line("Monitor pairs:");
        foreach (var pair in result.Pairs)
        {
            report.Line($"  {pair.First} vs {pair.Second}: centers {pair.CenterDistanceDegrees:0.0} degrees apart (yaw {pair.YawDifferenceDegrees:0.0}), d-prime {pair.DPrime:0.0} (clear needs 3.0 or more)");
        }

        report.Line();
        report.Line("== Camera and speed ==");
        report.Line($"Camera: {session.Camera.Description}");
        report.Line($"Face found in {result.FaceRate:P1} of recorded frames");
        report.Line($"Latency per frame: mean {meters.MeanMilliseconds:0.0} ms, worst {meters.MaxMilliseconds:0.0} ms");
        report.Line($"Frame rate: {meters.Fps:0.0} fps; process CPU {meters.CpuPercent:0.0}% of the whole PC ({meters.CpuPercentOfOneCore:0}% of one core)");
        report.Line($"Face finding: {meters.Find.Summary()}");
        report.Line($"Face detector: {(session.Pipeline.HasDetector ? "Windows FaceDetector" : "not available (centered box fallback)")}");
        if (result.FaceRate < MinimumFaceRate)
        {
            report.Line();
            report.Line($"The camera cannot see your face well (found in under {MinimumFaceRate:P0} of frames). Check that the room is");
            report.Line("well lit from the front (not a window behind you), the webcam is at eye level, and your face is fully in view.");
        }

        report.Line();
        report.Line("== Model ==");
        report.Line(session.Pipeline.ModelDescription);
        report.Line();
        report.Line($"Trace (numbers only) saved to: {tracePath}");
        report.Line($"Also saved to: {report.Path}");
    }

    private static string Position(int index, int count) => count switch
    {
        2 => index == 0 ? "LEFT" : "RIGHT",
        3 => index switch { 0 => "LEFT", 1 => "MIDDLE", _ => "RIGHT" },
        _ => $"number {index + 1} from the left",
    };

    // ---- shared plumbing ----

    private static bool StopRequested()
    {
        while (Console.KeyAvailable)
        {
            if (Console.ReadKey(true).Key == ConsoleKey.Enter)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The camera and the pipeline, started together and disposed together.</summary>
    private sealed class PoseSession : IDisposable
    {
        internal PoseSession(CameraSource camera, PosePipeline pipeline)
        {
            Camera = camera;
            Pipeline = pipeline;
        }

        internal CameraSource Camera { get; }

        internal PosePipeline Pipeline { get; }

        public void Dispose()
        {
            Camera.Dispose();
            Pipeline.Dispose();
        }
    }

    private static PoseSession? Open(Report report, out string? failure)
    {
        failure = null;
        var pipeline = PosePipeline.TryCreate(report.Line, out _);
        if (pipeline is null)
        {
            failure = "The face model could not be loaded, so the pose test cannot run. Use option c to see what the model looks like.";
            return null;
        }

        var camera = new CameraSource();
        var problem = camera.Start();
        if (problem is not null)
        {
            camera.Dispose();
            pipeline.Dispose();
            failure = problem;
            return null;
        }

        report.Line($"Camera started: {camera.Description}");
        return new PoseSession(camera, pipeline);
    }

    /// <summary>Per-frame latency, frame rate over the last couple of seconds, face rate, and process CPU.</summary>
    private sealed class Meters
    {
        private readonly Process _process = Process.GetCurrentProcess();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Queue<double> _recent = new();
        private readonly TimeSpan _cpuStart;
        private double _totalMilliseconds;
        private int _faces;

        internal Meters()
        {
            _process.Refresh();
            _cpuStart = _process.TotalProcessorTime;
        }

        internal FindStats Find { get; } = new();

        internal int Frames { get; private set; }

        internal double MaxMilliseconds { get; private set; }

        internal double MeanMilliseconds => Frames == 0 ? 0 : _totalMilliseconds / Frames;

        internal double FaceRate => Frames == 0 ? 0 : (double)_faces / Frames;

        internal double Fps
        {
            get
            {
                if (_recent.Count < 2)
                {
                    return 0;
                }

                var span = _recent.Last() - _recent.Peek();
                return span > 0 ? (_recent.Count - 1) / span : 0;
            }
        }

        /// <summary>CPU used by this process since the meter started, as a share of all logical processors.</summary>
        internal double CpuPercent => CpuPercentOfOneCore / Environment.ProcessorCount;

        internal double CpuPercentOfOneCore
        {
            get
            {
                _process.Refresh();
                var wall = _clock.Elapsed.TotalSeconds;
                return wall > 0 ? (_process.TotalProcessorTime - _cpuStart).TotalSeconds / wall * 100 : 0;
            }
        }

        internal void Add(PoseFrame frame)
        {
            Frames++;
            Find.Add(frame.Source);
            _totalMilliseconds += frame.Milliseconds;
            MaxMilliseconds = Math.Max(MaxMilliseconds, frame.Milliseconds);
            if (frame.Pose is not null)
            {
                _faces++;
            }

            var now = _clock.Elapsed.TotalSeconds;
            _recent.Enqueue(now);
            while (_recent.Count > 2 && now - _recent.Peek() > 2.0)
            {
                _recent.Dequeue();
            }
        }
    }
}
