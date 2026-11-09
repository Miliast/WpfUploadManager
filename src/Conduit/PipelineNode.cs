namespace Conduit;

/// <summary>
/// One pipeline node (e.g. "Payments" on Node 18) with its tests, plus a fake runner that advances them.
/// When the real runner exists, replace <see cref="Tick"/> with its events – the UI only reads the public state.
/// </summary>
internal sealed class PipelineNode
{
    /// <summary>Test states: 'q' queued, 'r' running, 'p' passed, 'f' failed, 'a' aborted.</summary>
    public List<(int No, string Name, char State, string Note, double Secs)> Tests { get; } = [];
    public string Name { get; }
    public string Runtime { get; }
    public bool Stopped { get; private set; }
    /// <summary>Bumped on every visible change, so the window only re-renders when needed.</summary>
    public int Version { get; private set; }
    /// <summary>Seconds the current test has been running (display time).</summary>
    public double RunningFor { get; private set; }

    public int Passed => Count('p');
    public int Failed => Count('f');
    public int Aborted => Count('a');
    public int Queued => Count('q');
    public int Done => Passed + Failed + Aborted;
    public int Total => Tests.Count;
    public int RunningIndex => Tests.FindIndex(t => t.State == 'r');
    public bool Finished => Stopped || Done == Total;
    public double Fraction => Total == 0 ? 1 : (double)Done / Total;

    private readonly Dictionary<int, (char State, string Note)> _planned;
    private readonly (char Kind, string Text, string Dim)[] _flavour;
    private readonly bool _randomFailures;
    private double _wall, _target;
    private int _flavourIndex;
    private bool _flavourDone;

    private PipelineNode(string name, string runtime, List<string> testNames, Dictionary<int, (char, string)> planned,
        (char, string, string)[] flavour, int seed, bool randomFailures)
    {
        Name = name;
        Runtime = runtime;
        _planned = planned;
        _flavour = flavour;
        _randomFailures = randomFailures;
        var rnd = new Random(seed);
        for (int i = 0; i < testNames.Count; i++)
        {
            int no = i + 1;
            double secs = planned.TryGetValue(no, out var p) && p.Item1 == 'a' ? 30.0 : Math.Round(0.2 + (rnd.NextDouble() * rnd.NextDouble() * 2.4), 1);
            Tests.Add((no, testNames[i], 'q', "", secs));
        }
    }

    /// <summary>Advances the fake runner by <paramref name="dt"/> wall-clock seconds and reports log lines.</summary>
    /// <param name="log">(kind, text, dim) – kind: R run, P pass, F fail, W warn, &gt; request, &lt; response.</param>
    public void Tick(double dt, Random rnd, Action<char, string, string> log)
    {
        if (Finished) return;
        int i = RunningIndex;
        if (i < 0)
        {
            Start(Tests.FindIndex(t => t.State == 'q'), log);
            return;
        }

        var test = Tests[i];
        _wall += dt;
        RunningFor = Math.Min(test.Secs, _wall / _target * test.Secs);

        if (!_flavourDone && _flavour.Length > 0 && _wall > _target * 0.5 && rnd.NextDouble() < 0.35)
        {
            var f = _flavour[_flavourIndex++ % _flavour.Length];
            log(f.Kind, f.Text, f.Dim);
            _flavourDone = true;
        }
        if (_wall < _target) return;

        // Finish the current test …
        var (state, note) = _planned.TryGetValue(test.No, out var p) ? p
            : _randomFailures && rnd.NextDouble() < 0.03 ? ('f', RandomFailure(rnd))
            : _randomFailures && rnd.NextDouble() < 0.015 ? ('a', "runner lost connection")
            : ('p', "");
        if (state == 'a') test.Secs = 30.0;
        Tests[i] = (test.No, test.Name, state, note, test.Secs);
        string secs = Ui.Inv($"{test.Secs:0.0}s");
        switch (state)
        {
            case 'p': log('P', $"#{test.No} {test.Name}", secs); break;
            case 'f': log('F', $"#{test.No} {test.Name}", note); break;
            default: log('W', $"#{test.No} {test.Name} aborted: {note}", ""); break;
        }
        // … and start the next one right away.
        Start(Tests.FindIndex(t => t.State == 'q'), log);
    }

    public void Stop()
    {
        if (Finished) return;
        int i = RunningIndex;
        if (i >= 0) Tests[i] = Tests[i] with { State = 'q' };
        Stopped = true;
        Version++;
    }

    private void Start(int index, Action<char, string, string> log)
    {
        Version++;
        if (index < 0) return;
        var t = Tests[index];
        Tests[index] = t with { State = 'r' };
        _wall = 0;
        RunningFor = 0;
        _flavourDone = false;
        // Wall-clock time of the demo: a compressed version of the test's own duration.
        _target = 0.35 + (Math.Min(t.Secs, 3) * 0.3);
        log('R', $"#{t.No} {t.Name}", "");
    }

    private int Count(char state)
    {
        int n = 0;
        foreach (var t in Tests) if (t.State == state) n++;
        return n;
    }

    private static string RandomFailure(Random rnd) => rnd.Next(3) switch
    {
        0 => "expected 200, received 500",
        1 => "timeout after 5000ms",
        _ => "snapshot mismatch",
    };

    // ── Factories ──────────────────────────────────────────────────────────

    /// <summary>The demo data from the Paper design (Payments 50, Formatting 60, Queue worker 50 = 160 tests).</summary>
    public static List<PipelineNode> Sample(bool single)
    {
        var payments = new PipelineNode("Payments", "Node 18",
            Names(["creates a", "updates a", "validates the", "cancels a", "lists every"],
                ["customer", "invoice", "payout", "subscription", "coupon", "dispute", "tax rate", "refund", "price", "mandate"], 50,
                (20, "retries a failed webhook"), (21, "applies currency rounding"), (22, "sends receipt email"),
                (23, "rejects expired cards"), (24, "stores card token securely"), (25, "charges a saved card")),
            new() { [20] = ('f', "expected 200, received 500"), [22] = ('a', "runner lost connection") },
            [
                ('>', "POST /v1/charges", "{ amount: 1299, currency: \"eur\" }"),
                ('<', "201 Created", "ch_3PqLx2 · 571ms"),
                ('W', "webhook delivery slow, retrying in 250ms", ""),
                ('>', "GET /v1/customers/cus_92", ""),
                ('<', "200 OK", "cus_92 · 88ms"),
            ], 18, false);
        if (single) return [payments];

        var formatting = new PipelineNode("Formatting", "Node 20",
            Names(["formats", "parses", "rounds", "localizes", "pads"],
                ["dates", "currency per locale", "numbers", "percentages", "plurals", "addresses", "phone numbers", "time zones", "lists", "units", "durations", "file sizes"], 60),
            new(),
            [('>', "load locale de-DE", ""), ('<', "locale ready", "41ms"), ('>', "load locale ja-JP", "")], 20, false);

        var queue = new PipelineNode("Queue worker", "Node 22",
            Names(["enqueues", "processes", "acks", "requeues", "drops"],
                ["a job", "a batch", "a delayed job", "a priority job", "a cron job", "a webhook event", "an email", "an export", "a retry", "a large payload"], 50,
                (28, "dead-letters after 3 attempts")),
            new() { [28] = ('a', "worker restarted mid-test") },
            [('>', "PUSH jobs:default", "id=8812"), ('<', "ACK", "8812 · 12ms"), ('W', "queue depth above 500", "")], 22, false);

        return [payments, formatting, queue];
    }

    /// <summary>A node for a real test folder. Tests are simulated, roughly 4 per spec file.</summary>
    public static PipelineNode ForFolder(string name, int specFiles, int seed) => new(
        name, "", Names(["runs", "checks", "verifies", "covers", "asserts"],
            ["happy path", "empty input", "invalid input", "edge cases", "error handling", "retries", "timeouts", "permissions", "pagination", "caching"],
            Math.Clamp(specFiles * 4, 12, 120)),
        new(), [('>', "spawn worker", ""), ('<', "worker ready", "")], seed, true);

    /// <summary>verb × noun test names, with fixed names at given (1-based) positions.</summary>
    private static List<string> Names(string[] verbs, string[] nouns, int count, params (int No, string Name)[] fixedNames)
    {
        var names = new List<string>(count);
        for (int i = 0; names.Count < count; i++)
            names.Add($"{verbs[(i / nouns.Length) % verbs.Length]} {nouns[i % nouns.Length]}" + (i >= verbs.Length * nouns.Length ? $" #{i}" : ""));
        foreach (var (no, name) in fixedNames) names[no - 1] = name;
        return names;
    }
}
