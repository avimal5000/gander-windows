using Gander.Tools.Spikes;

Console.WriteLine("GanderTools: spike programs for Gander on Windows");
Console.WriteLine();
Console.WriteLine("  1  System info: Windows, cameras, monitors, windows   (S3)");
Console.WriteLine("  2  Focus test: move focus between monitors             (S2)");
Console.WriteLine("  3  Pose test: head yaw/pitch, do the monitors separate?  (S1)");
Console.WriteLine("       3a live readout, 3b gate run, 3c describe the face model (no camera)");
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
    case "3" or "pose":
        PoseSpike.Run(null);
        break;
    case "3a" or "3b" or "3c":
        PoseSpike.Run(choice[1..]);
        break;
    case "--describe-model":
        PoseSpike.Run("c");
        break;
    default:
        return;
}

Console.WriteLine();
Console.Write("Press Enter to close.");
Console.ReadLine();
