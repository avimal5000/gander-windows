using System.Reflection;

var version = Assembly.GetExecutingAssembly().GetName().Version;
Console.WriteLine($"SwayTools {version}");
Console.WriteLine("The W1 spike programs (pose, focus, monitors) arrive in the next milestone.");
