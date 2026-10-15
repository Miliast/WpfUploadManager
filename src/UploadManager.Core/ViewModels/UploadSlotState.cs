namespace UploadManager.Core.ViewModels;

public enum UploadSlotState
{
    /// <summary>No file assigned.</summary>
    Empty,

    /// <summary>A valid file is assigned and waits for upload.</summary>
    Ready,

    /// <summary>A file is assigned but violates the slot rules.</summary>
    Invalid,

    /// <summary>The file is being uploaded.</summary>
    Uploading,

    /// <summary>The file was uploaded successfully.</summary>
    Completed,
}
