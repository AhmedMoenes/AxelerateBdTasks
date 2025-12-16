using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB.Events;
using Nice3point.Revit.Toolkit.External;
using Task5.Handlers;

namespace Task5;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class Application : ExternalApplication
{
    private SectionBoxHandler _sectionBoxHandler;

    public override void OnStartup()
    {
        _sectionBoxHandler = new SectionBoxHandler();
        Application.ControlledApplication.DocumentChanged += OnDocumentChanged;
    }

    public override void OnShutdown()
    {
        Application.ControlledApplication.DocumentChanged -= OnDocumentChanged;
        _sectionBoxHandler?.Dispose();
    }

    private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
    {
        _sectionBoxHandler.HandleDocumentChanged(sender, e);
    }
}