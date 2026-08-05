namespace ItTiger.TigerQuery.TestChild;

/// <summary>
/// A deterministic child process for the <c>tiger-sqlcmd exec</c> tests.
/// </summary>
/// <remarks>
/// <para>
/// It reports exactly what it received, so a test can assert on exact argument and
/// environment transfer without depending on SqlPackage or any other external tool. Every
/// argument is echoed to standard output as <c>ARG[i]=&lt;value&gt;</c>, byte for byte and
/// in order, preceded by <c>ARGC=&lt;count&gt;</c>.
/// </para>
/// <para>
/// Three of its own arguments also select behavior. They are still echoed like any other
/// argument, so nothing is hidden from the assertions:
/// </para>
/// <list type="bullet">
///   <item><c>--echo-env NAME</c> writes <c>ENV[NAME]=&lt;value&gt;</c>, or
///     <c>ENV[NAME]=(unset)</c>. Repeatable.</item>
///   <item><c>--stderr TEXT</c> writes TEXT to standard error.</item>
///   <item><c>--exit N</c> exits with N instead of 0.</item>
/// </list>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.Out.WriteLine($"ARGC={args.Length}");
        for (var index = 0; index < args.Length; index++)
            Console.Out.WriteLine($"ARG[{index}]={args[index]}");

        var exitCode = 0;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--echo-env" when index + 1 < args.Length:
                    var name = args[++index];
                    Console.Out.WriteLine(
                        $"ENV[{name}]={Environment.GetEnvironmentVariable(name) ?? "(unset)"}");
                    break;

                case "--stderr" when index + 1 < args.Length:
                    Console.Error.WriteLine(args[++index]);
                    break;

                case "--exit" when index + 1 < args.Length && int.TryParse(args[index + 1], out var requested):
                    index++;
                    exitCode = requested;
                    break;
            }
        }

        Console.Out.WriteLine($"CWD={Environment.CurrentDirectory}");
        Console.Out.Flush();
        Console.Error.Flush();
        return exitCode;
    }
}
