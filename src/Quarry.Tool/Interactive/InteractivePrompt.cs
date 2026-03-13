namespace Quarry.Tool.Interactive;

internal static class InteractivePrompt
{
    public static bool IsInteractive(bool nonInteractive)
    {
        return !nonInteractive && !Console.IsInputRedirected;
    }

    public static bool Confirm(string message)
    {
        Console.Write(message + " [Y/n]: ");
        var key = Console.ReadLine()?.Trim().ToLowerInvariant();
        return key is "" or "y" or "yes";
    }

    public static T Choose<T>(string message, IReadOnlyList<(string Label, T Value)> options)
    {
        Console.WriteLine(message);
        for (var i = 0; i < options.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {options[i].Label}");
        }
        Console.Write("Choice: ");
        if (int.TryParse(Console.ReadLine()?.Trim(), out var choice) && choice >= 1 && choice <= options.Count)
        {
            return options[choice - 1].Value;
        }
        return options[0].Value;
    }
}
