// [Nori Modification] Guarded Dispose against native double-free.
using System.Runtime.InteropServices;
using Live2DCSharpSDK.Framework.Core;

namespace Live2DCSharpSDK.Framework.Model;

/// <summary>
/// Mocデータの管理を行うクラス。
/// </summary>
public class CubismMoc : IDisposable
{
    /// <summary>
    /// Mocデータ
    /// </summary>
    private readonly IntPtr _moc;
    /// <summary>
    /// 読み込んだモデルの.moc3 Version
    /// </summary>
    public uint MocVersion { get; }

    public CubismModel Model { get; }

    /// <summary>
    /// バッファからMocファイルを読み取り、Mocデータを作成する。
    /// </summary>
    /// <param name="mocBytes"> Mocファイルのバッファ</param>
    /// <param name="shouldCheckMocConsistency">MOCの整合性チェックフラグ(初期値 : false)</param>
    /// <returns></returns>
    public CubismMoc(byte[] mocBytes, bool shouldCheckMocConsistency = false)
    {
        IntPtr alignedBuffer = CubismFramework.AllocateAligned(mocBytes.Length, CsmEnum.csmAlignofMoc);
        Marshal.Copy(mocBytes, 0, alignedBuffer, mocBytes.Length);

        if (shouldCheckMocConsistency)
        {
            // .moc3の整合性を確認
            bool consistency = HasMocConsistency(alignedBuffer, mocBytes.Length);
            if (!consistency)
            {
                CubismFramework.DeallocateAligned(alignedBuffer);

                // 整合性が確認できなければ処理しない
                throw new Exception("Inconsistent MOC3.");
            }
        }

        var moc = CubismCore.ReviveMocInPlace(alignedBuffer, mocBytes.Length);

        if (moc == IntPtr.Zero)
        {
            throw new Exception("MOC3 is null");
        }

        _moc = moc;

        MocVersion = CubismCore.GetMocVersion(alignedBuffer, mocBytes.Length);

        var modelSize = CubismCore.GetSizeofModel(_moc);
        var modelMemory = CubismFramework.AllocateAligned(modelSize, CsmEnum.CsmAlignofModel);

        var model = CubismCore.InitializeModelInPlace(_moc, modelMemory, modelSize);

        if (model == IntPtr.Zero)
        {
            throw new Exception("MODEL is null");
        }

        Model = new CubismModel(model);
    }

    /// <summary>
    /// 最新の.moc3 Versionを取得する。
    /// </summary>
    /// <returns></returns>
    public static uint GetLatestMocVersion()
    {
        return CubismCore.GetLatestMocVersion();
    }

    /// <summary>
    /// Checks consistency of a moc.
    /// </summary>
    /// <param name="address">Address of unrevived moc. The address must be aligned to 'csmAlignofMoc'.</param>
    /// <param name="size">Size of moc (in bytes).</param>
    /// <returns>'1' if Moc is valid; '0' otherwise.</returns>
    public static bool HasMocConsistency(IntPtr address, int size)
    {
        return CubismCore.HasMocConsistency(address, size);
    }

    /// <summary>
    /// Checks consistency of a moc.
    /// </summary>
    /// <param name="mocBytes">Mocファイルのバッファ</param>
    /// <param name="size">バッファのサイズ</param>
    /// <returns>'true' if Moc is valid; 'false' otherwise.</returns>
    public static bool HasMocConsistencyFromUnrevivedMoc(byte[] data)
    {
        IntPtr alignedBuffer = CubismFramework.AllocateAligned(data.Length, CsmEnum.csmAlignofMoc);
        Marshal.Copy(data, 0, alignedBuffer, data.Length);

        bool consistency = HasMocConsistency(alignedBuffer, data.Length);

        CubismFramework.DeallocateAligned(alignedBuffer);

        return consistency;
    }

    /// <summary>
    /// デストラクタ。
    /// </summary>
    /// <summary>
    /// [Nori Modification] 重入保护
    ///
    /// 原实现无条件 DeallocateAligned(_moc), 被 Dispose 两次就是非托管内存的双重释放,
    /// 表现为进程直接以 0xC0000374 (STATUS_HEAP_CORRUPTION) 崩溃且没有托管栈。
    /// </summary>
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Model.Dispose();
        CubismFramework.DeallocateAligned(_moc);
        GC.SuppressFinalize(this);
    }
}
