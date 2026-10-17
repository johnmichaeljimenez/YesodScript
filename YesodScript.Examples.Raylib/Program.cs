namespace YesodScript.Examples;

public static class Program
{
	[STAThread]
	public static void Main()
	{
		RaylibHost? host = null;
		try
		{
			var vm = GameVM.Create();
			host = new RaylibHost(vm);
			host.Tick();
		}
		catch (Exception ex)
		{
			
			ErrorReport.Write(ex, "Startup");
			ErrorReport.ShowMessageBox(ErrorReport.FormatMessageBox(ex, "Startup"));
		}
		finally
		{
			try
			{
				host?.Cleanup();
			}
			catch
			{
				
			}
		}
	}
}