namespace SpaceMouse.App.Services;

/// <summary>Implemented by pages that want to refresh when they become visible.</summary>
public interface IPage
{
    void OnShown();
}
