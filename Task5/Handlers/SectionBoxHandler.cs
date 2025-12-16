using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Task5.Models;

namespace Task5.Handlers;

public class SectionBoxHandler : IDisposable
{
    private const double SECTION_OFFSET = 10.0; // feet

    private readonly List<ViewSectionData> _pendingSections;
    private UIApplication _uiApplication;

    public SectionBoxHandler()
    {
        _pendingSections = new List<ViewSectionData>();
    }

    public void HandleDocumentChanged(object sender, DocumentChangedEventArgs e)
    {
        var document = e.GetDocument();
        var activeView = document.ActiveView;

        // Only process floor plan views
        if (!IsValidFloorPlanView(activeView))
            return;

        var level = activeView.GenLevel;
        var levelElevation = level.Elevation;

        // Check for newly created section views
        var addedElementIds = e.GetAddedElementIds();
        var newSections = CollectNewSections(document, addedElementIds, levelElevation);

        if (newSections.Count == 0)
            return;

        _pendingSections.AddRange(newSections);

        if (_uiApplication == null)
        {
            _uiApplication = new UIApplication(document.Application);
            _uiApplication.Idling += OnIdling;
        }
    }

    private bool IsValidFloorPlanView(View view)
    {
        return view.ViewType == ViewType.FloorPlan && view.GenLevel != null;
    }

    private List<ViewSectionData> CollectNewSections(
        Document document,
        ICollection<ElementId> elementIds,
        double levelElevation)
    {
        var sections = new List<ViewSectionData>();

        foreach (var elementId in elementIds)
        {
            var element = document.GetElement(elementId);

            if (element is ViewSection)
            {
                sections.Add(new ViewSectionData
                {
                    SectionId = elementId,
                    LevelElevation = levelElevation
                });
            }
        }

        return sections;
    }

    private void OnIdling(object sender, IdlingEventArgs e)
    {
        var uiApplication = sender as UIApplication;
        if (uiApplication == null)
            return;

        uiApplication.Idling -= OnIdling;

        var uiDocument = uiApplication.ActiveUIDocument;
        if (uiDocument == null)
        {
            _pendingSections.Clear();
            return;
        }

        ProcessPendingSections(uiDocument.Document);
    }

    private void ProcessPendingSections(Document document)
    {
        if (_pendingSections.Count == 0)
            return;

        using var transaction = new Transaction(document, "Modify Section Boxes");
        transaction.Start();

        try
        {
            var processedCount = 0;

            foreach (var sectionData in _pendingSections)
            {
                if (ModifySectionBox(document, sectionData))
                {
                    processedCount++;
                }
            }

            transaction.Commit();

            if (processedCount > 0)
            {
                TaskDialog.Show("Success",
                    $"{processedCount} section box(es) modified successfully");
            }
        }
        catch (Exception ex)
        {
            transaction.RollBack();
            TaskDialog.Show("Error", $"Failed to modify section boxes: {ex.Message}");
        }
        finally
        {
            _pendingSections.Clear();
        }
    }

    private bool ModifySectionBox(Document document, ViewSectionData sectionData)
    {
        var viewSection = document.GetElement(sectionData.SectionId) as ViewSection;

        if (viewSection == null || !viewSection.CropBoxActive)
            return false;

        var cropBox = viewSection.CropBox;

        var newBoundingBox = new BoundingBoxXYZ
        {
            Min = new XYZ(
                cropBox.Min.X,
                cropBox.Min.Y,
                sectionData.LevelElevation - SECTION_OFFSET),
            Max = new XYZ(
                cropBox.Max.X,
                cropBox.Max.Y,
                sectionData.LevelElevation + SECTION_OFFSET)
        };

        viewSection.CropBox = newBoundingBox;
        viewSection.CropBoxActive = true;
        viewSection.CropBoxVisible = true;

        return true;
    }

    public void Dispose()
    {
        if (_uiApplication != null)
        {
            _uiApplication.Idling -= OnIdling;
            _uiApplication = null;
        }

        _pendingSections.Clear();
    }
}