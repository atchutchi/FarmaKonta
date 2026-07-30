using System.Security.Cryptography;
using System.Text;

namespace Nofarma.Licensing.Qa;

public interface IQaKeyStoreLock
{
    IDisposable Acquire();
}

internal sealed class QaNamedKeyStoreLock
    : IQaKeyStoreLock
{
    private static readonly TimeSpan AcquisitionTimeout = TimeSpan.FromSeconds(30);

    [ThreadStatic]
    private static HashSet<string>? _heldNames;

    private readonly string _name;

    internal QaNamedKeyStoreLock(string privateKeyPath)
    {
        string canonicalPath = Path.GetFullPath(privateKeyPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (OperatingSystem.IsWindows())
        {
            canonicalPath = canonicalPath.ToUpperInvariant();
        }

        byte[] pathBytes = Encoding.UTF8.GetBytes(canonicalPath);
        byte[] hash = SHA256.HashData(pathBytes);
        try
        {
            string prefix = OperatingSystem.IsWindows() ? @"Local\" : string.Empty;
            _name = string.Concat(
                prefix,
                "ABIPTOM.Nofarma.QA.Key.",
                Convert.ToHexString(hash));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pathBytes);
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    public IDisposable Acquire()
    {
        _heldNames ??= [];
        if (!_heldNames.Add(_name))
        {
            throw new QaIssuerException(
                "QA_KEY_LOCK_REENTRANCY",
                "A reentrant QA key-store operation was rejected.");
        }

        Mutex? mutex = null;
        bool acquired = false;
        try
        {
            mutex = new Mutex(initiallyOwned: false, _name);
            try
            {
                acquired = mutex.WaitOne(AcquisitionTimeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new QaIssuerException(
                    "QA_KEY_LOCK_TIMEOUT",
                    "The QA key store is busy in another process.");
            }

            return new MutexLease(mutex, _name);
        }
        catch
        {
            mutex?.Dispose();
            _heldNames.Remove(_name);
            throw;
        }
    }

    private sealed class MutexLease(Mutex mutex, string name) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                mutex.ReleaseMutex();
            }
            finally
            {
                mutex.Dispose();
                _heldNames?.Remove(name);
            }
        }
    }
}
