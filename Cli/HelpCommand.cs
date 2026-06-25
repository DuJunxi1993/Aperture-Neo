using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using System.Threading.Tasks;

namespace ApertureNeo.Cli;

/// <summary>
/// `aperture help [command]` — explicit help entry point that
/// delegates to System.CommandLine's auto-help for the
/// RootCommand or any subcommand. Without arguments, shows the
/// root help (all commands + global options + configuration).
/// With a command name, shows that command's help.
///
/// The double-source pattern (rich Description strings +
/// System.CommandLine's auto-help footer) means the output of
/// `help`, `help ocr`, `--help`, and `ocr --help` is
/// consistent — there is one source of truth per command (its
/// Description) and one renderer (System.CommandLine's
/// HelpBuilder), kept in sync by delegation.
///
/// Why not just use `--help`? Two reasons:
///   1. `help` reads naturally on the command line and is the
///      verb users reach for first (mirrors `git help`,
///      `cargo help`, `dotnet help`).
///   2. `help <cmd>` is a single, memorable pattern; users
///      don't have to remember the difference between
///      `--help` (flag on root) and `<cmd> --help` (flag on
///      a subcommand).
///
/// The handler is sync (Action<InvocationContext>) and uses
/// Task.Run to call root.Invoke on a thread-pool thread. The
/// async-on-UI-thread path deadlocks because System.
/// CommandLine's help writer posts to the dispatcher that is
/// currently blocked on the outer InvokeAsync. Running the
/// inner Invoke off the UI thread breaks the cycle.
/// </summary>
public sealed class HelpCommand : Command
{
    private readonly Func<Command> _rootProvider;

    public HelpCommand(Func<Command> rootProvider)
        : base("help",
              "Show detailed help. Without arguments, lists all commands and global options. " +
              "With a command name, shows help for that command.")
    {
        _rootProvider = rootProvider;

        var commandArg = new Argument<string?>("command")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "The command to show help for (e.g., 'ocr', 'plugin-list'). " +
                          "Omit to show overall help."
        };
        AddArgument(commandArg);

        // The recursive Invoke to render the help is run on a
        // thread pool thread (Task.Run + .Result) to avoid a
        // SynchronizationContext deadlock: the outer InvokeAsync
        // runs on the WPF UI thread, and the inner one's help
        // writer can post to that same context, which is blocked
        // waiting for the inner to finish.
        //
        // We validate the command name up front so unknown names
        // produce a clear "Unknown command: foo" error with exit 1
        // instead of falling through to the root help (which would
        // be confusing — the user asked for help on a specific
        // command, not for the general overview).
        this.SetHandler((InvocationContext ctx) =>
        {
            var root = _rootProvider();
            var cmdName = ctx.ParseResult.GetValueForArgument(commandArg);
            string[] helpArgs;

            if (string.IsNullOrEmpty(cmdName))
            {
                helpArgs = new[] { "--help" };
            }
            else
            {
                // Case-insensitive lookup: RootCommand.Children.OfType<Command>()
                // stores Command instances keyed by name. We match the user's
                // intent even if they typed "OCR" or "Help".
                var match = root.Children
                    .OfType<Command>()
                    .FirstOrDefault(c => string.Equals(c.Name, cmdName, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    Console.Error.WriteLine($"Unknown command: {cmdName}");
                    Console.Error.WriteLine($"Run '{root.Name} help' for the list of available commands.");
                    ctx.ExitCode = 1;
                    return;
                }
                helpArgs = new[] { match.Name, "--help" };
            }

            int exitCode = Task.Run(() => root.Invoke(helpArgs)).GetAwaiter().GetResult();
            ctx.ExitCode = exitCode;
        });
    }
}
