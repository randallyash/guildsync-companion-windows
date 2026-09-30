namespace GuildSync.Companion.Services;

public interface IUserDialogs
{
    Task<string?> PickFolderAsync();
    void OpenUrl(string url);
    void ShowToast(string message);
    void ShowWindow();
    void Shutdown();
}
