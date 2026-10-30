using Sway.Tools.Spikes;

Console.WriteLine("SwayTools: spike programs for Sway on Windows");
Console.WriteLine();
Console.WriteLine("  1  System info: Windows, cameras, monitors, windows   (S3)");
Console.WriteLine("  2  Focus test: move focus between monitors             (S2)");
Console.WriteLine("  q  Quit");
Console.WriteLine();

var choice = args.Length > 0 ? args[0] : null;
if (choice is null)
{
    Console.Write("Choose: ");
    choice = Console.ReadLine()?.Trim();
}

Console.WriteLine();
switch (choice)
{
    case "1" or "info":
        InfoSpike.Run();
        break;
    case "2" or "focus":
        FocusSpike.Run();
        break;
    default:
        return;
}

Console.WriteLine();
Console.Write("Press Enter to close.");
Console.ReadLine();
