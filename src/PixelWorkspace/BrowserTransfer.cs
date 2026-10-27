using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace PixelWorkspace;

/// <summary>
/// Destination for a file selected in <see cref="BrowserPage"/>.
/// </summary>
public enum BrowserTransferTarget
{
    Editor,
    Obfuscation,
    Metadata
}

/// <summary>
/// A browser-to-tool transfer request. The path is kept as a path so each
/// destination can load it using its existing decoder and preserve its state.
/// </summary>
public sealed class BrowserTransferRequestedEventArgs : EventArgs
{
    public BrowserTransferRequestedEventArgs(string path, BrowserTransferTarget target)
    {
        Path = path;
        Target = target;
    }

    public string Path { get; }
    public BrowserTransferTarget Target { get; }
}

/// <summary>
/// Shared handoff for the browser's selected file. This is intentionally
/// independent of navigation and page instances; the host owns both of those.
/// </summary>
public static class BrowserTransfer
{
    private static readonly object Sync = new();
    private static string? lastPath;

    public static event EventHandler<BrowserTransferRequestedEventArgs>? Requested;

    public static string? LastPath
    {
        get { lock (Sync) return lastPath; }
    }

    /// <summary>Publishes a selected file and notifies the host.</summary>
    public static bool Publish(string path, BrowserTransferTarget target)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        path = System.IO.Path.GetFullPath(path);
        lock (Sync) lastPath = path;
        Requested?.Invoke(null, new BrowserTransferRequestedEventArgs(path, target));
        return true;
    }

    /// <summary>
    /// Loads a path into an existing destination page without opening another
    /// file dialog. The page instances remain cached by the host, so their
    /// current tool state is preserved.
    /// </summary>
    public static Task ImportAsync(object destination, BrowserTransferTarget target, string path)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Transfer file was not found.", path);

        MethodInfo method;
        object[] arguments;
        switch (target)
        {
            case BrowserTransferTarget.Editor:
                method = Find(destination, "LoadFiles", typeof(IEnumerable<string>), typeof(bool));
                arguments = new object[] { new[] { path }, false };
                break;
            case BrowserTransferTarget.Obfuscation:
            case BrowserTransferTarget.Metadata:
                method = Find(destination, "AddFiles", typeof(string[]));
                arguments = new object[] { new[] { path } };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }

        object? result;
        try
        {
            result = method.Invoke(destination, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }

        return result as Task ?? Task.CompletedTask;
    }

    private static MethodInfo Find(object destination, string name, params Type[] parameterTypes)
    {
        var method = destination.GetType().GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null);
        return method ?? throw new MissingMethodException(destination.GetType().FullName, name);
    }
}
