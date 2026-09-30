using System;
using System.Collections.Generic;
using System.Linq;

namespace IfcComBridge.Cli
{
    public sealed class UsageException : Exception
    {
        public UsageException(string message) : base(message) { }
    }

    /// <summary>
    /// Minimal "--name value" / "--flag" parser. The harness deliberately avoids a third-party
    /// command-line dependency. Unknown or unused options are errors, so a typo can never silently
    /// fall back to a default.
    /// </summary>
    internal sealed class CommandArguments
    {
        private readonly Dictionary<string, string> _options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private CommandArguments(string command)
        {
            Command = command;
        }

        public string Command { get; }

        public static CommandArguments Parse(IReadOnlyList<string> args, ISet<string> flagNames)
        {
            if (args.Count == 0)
                throw new UsageException("No command given.");

            var result = new CommandArguments(args[0].ToLowerInvariant());
            for (int i = 1; i < args.Count; i++)
            {
                string token = args[i];
                if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length < 3)
                    throw new UsageException($"Unexpected argument '{token}'. Options must look like --name value.");

                string name = token.Substring(2);
                if (flagNames.Contains(name))
                {
                    result._flags.Add(name);
                    continue;
                }

                if (i + 1 >= args.Count)
                    throw new UsageException($"Option --{name} needs a value.");
                if (result._options.ContainsKey(name))
                    throw new UsageException($"Option --{name} was given more than once.");

                result._options[name] = args[++i];
            }

            return result;
        }

        public string Required(string name)
        {
            string value = Optional(name);
            if (string.IsNullOrWhiteSpace(value))
                throw new UsageException($"Missing required option --{name}.");
            return value;
        }

        public string Optional(string name)
        {
            _consumed.Add(name);
            return _options.TryGetValue(name, out string value) ? value : null;
        }

        public int OptionalInt(string name, int defaultValue)
        {
            string value = Optional(name);
            if (value == null)
                return defaultValue;
            if (!int.TryParse(value, out int parsed))
                throw new UsageException($"Option --{name} must be an integer.");
            return parsed;
        }

        public bool Flag(string name)
        {
            _consumed.Add(name);
            return _flags.Contains(name);
        }

        /// <summary>Call after reading every option the command understands.</summary>
        public void EnsureAllConsumed()
        {
            var unknown = _options.Keys.Concat(_flags).Where(n => !_consumed.Contains(n)).ToList();
            if (unknown.Count > 0)
                throw new UsageException($"Unknown option(s) for '{Command}': {string.Join(", ", unknown.Select(n => "--" + n))}.");
        }
    }
}
