namespace TechoramaOpenAI.Services;

public class ToastService
{
    public event Func<string, string, Task<bool>>? OnShowApprovalToast;

    public async Task<bool> ShowApprovalToastAsync(string title, string message)
    {
        if (OnShowApprovalToast != null)
        {
            return await OnShowApprovalToast.Invoke(title, message);
        }
        return false;
    }
}