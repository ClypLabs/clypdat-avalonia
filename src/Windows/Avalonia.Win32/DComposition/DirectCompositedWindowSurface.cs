using System;
using System.Diagnostics.CodeAnalysis;

using Avalonia.OpenGL.Egl;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Win32.DirectX;
using Avalonia.Win32.Interop;

using MicroCom.Runtime;

namespace Avalonia.Win32.DComposition;

internal class DirectCompositedWindowSurface : IDirect3D11TexturePlatformSurface, IDirect3D11TexturePlatformSurface2, IDisposable, ICompositionEffectsSurface
{
    private readonly EglGlPlatformSurface.IEglWindowGlPlatformSurfaceInfo _info;
    private readonly DirectCompositionShared _shared;
    private DirectCompositedWindow? _window;
    private BlurEffect _blurEffect;

    public DirectCompositedWindowSurface(DirectCompositionShared shared, EglGlPlatformSurface.IEglWindowGlPlatformSurfaceInfo info)
    {
        _shared = shared;
        _info = info;
    }

    IDirect3D11TextureRenderTarget IDirect3D11TexturePlatformSurface.CreateRenderTarget(IPlatformGraphicsContext context, IntPtr d3dDevice)
    {
        return (IDirect3D11TextureRenderTarget) CreateRenderTarget(context, d3dDevice);
    }

    public IDirect3D11TextureRenderTarget2 CreateRenderTarget(IPlatformGraphicsContext context, IntPtr d3dDevice)
    {
        _window ??= new DirectCompositedWindow(_info, _shared);
        SetBlur(_blurEffect);

        return new DirectCompositedWindowRenderTarget(context, d3dDevice, _shared, _window);
    }

    public void Dispose()
    {
        _window?.Dispose();
        _window = null;
    }

    // TODO: we can implement BlurEffect.GaussianBlur in with IDCompositionDevice3.CreateGaussianBlurEffect. 
    public bool IsBlurSupported(BlurEffect effect) => effect == BlurEffect.None;

    public void SetBlur(BlurEffect enable)
    {
        _blurEffect = enable;
        // _window?.SetBlur(enable);
    }
}

internal class DirectCompositedWindowRenderTarget : IDirect3D11TextureRenderTarget, IDirect3D11TextureRenderTarget2
{
    private static readonly Guid IID_ID3D11Texture2D = Guid.Parse("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    private readonly IPlatformGraphicsContext _context;
    private readonly DirectCompositionShared _shared;
    private readonly DirectCompositedWindow _window;
    private SurfaceSet? _activeSurface;
    private bool _lost;
    private readonly IUnknown _d3dDevice;

    private sealed class SurfaceSet : IDisposable
    {
        public readonly IDCompositionVirtualSurface Surface;
        public readonly PixelSize Size;
        public readonly bool SupportsTransparency;

        public SurfaceSet(IDCompositionVirtualSurface surface, PixelSize size, bool supportsTransparency)
        {
            Surface = surface;
            Size = size;
            SupportsTransparency = supportsTransparency;
        }

        public void Dispose() => Surface.Dispose();
    }

    public DirectCompositedWindowRenderTarget(
        IPlatformGraphicsContext context, IntPtr d3dDevice,
        DirectCompositionShared shared, DirectCompositedWindow window)
    {
        _d3dDevice = MicroComRuntime.CreateProxyFor<IUnknown>(d3dDevice, false).CloneReference();

        _context = context;
        _shared = shared;
        _window = window;
    }

    private SurfaceSet CreateSurface(in IRenderTarget.RenderTargetSceneInfo sceneInfo)
    {
        using var surfaceFactory = _shared.Device.CreateSurfaceFactory(_d3dDevice);

        bool isTransparency = sceneInfo.TransparencyLevel != CompositionTransparencyLevel.None;
        var surfaceSize = sceneInfo.Size;

        var alphaMode = isTransparency ?
            DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_PREMULTIPLIED :
            DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_IGNORE;

        var surface = surfaceFactory.CreateVirtualSurface((uint)surfaceSize.Width, (uint)surfaceSize.Height,
            DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, alphaMode);
        return new SurfaceSet(surface, surfaceSize, isTransparency);
    }

    public void Dispose()
    {
        _activeSurface?.Dispose();
        _d3dDevice.Dispose();
    }

    public PlatformRenderTargetState State => _context.IsLost || _lost ? PlatformRenderTargetState.Corrupted : PlatformRenderTargetState.Ready;

    IDirect3D11TextureRenderTargetRenderSession IDirect3D11TextureRenderTarget.BeginDraw()
    {
        var fallbackSceneInfo = new IRenderTarget.RenderTargetSceneInfo(_window.WindowInfo.Size,
            _window.WindowInfo.Scaling, CompositionTransparencyLevel.None);
        return BeginDraw(fallbackSceneInfo);
    }

    public unsafe IDirect3D11TextureRenderTargetRenderSession BeginDraw(IRenderTarget.RenderTargetSceneInfo sceneInfo)
    {
        if (State.IsCorrupted)
            throw new RenderTargetCorruptedException();
        var transaction = _window.BeginTransaction();
        bool needsEndDraw = false;
        SurfaceSet? drawSurface = null;
        try
        {
            bool isTransparency = sceneInfo.TransparencyLevel != CompositionTransparencyLevel.None;
            var size = sceneInfo.Size;
            var scale = sceneInfo.Scaling;
            var previousSurface = _activeSurface;
            var replacement = previousSurface is null || previousSurface.Size != size ||
                              previousSurface.SupportsTransparency != isTransparency;
            drawSurface = replacement ? CreateSurface(in sceneInfo) : previousSurface;
                
            void* pTexture;
            UnmanagedMethods.POINT off;
            try
            {
                var rect = new UnmanagedMethods.RECT { right = size.Width, bottom = size.Height };
                var iid = IID_ID3D11Texture2D;
                off = drawSurface!.Surface.BeginDraw(&rect, &iid, &pTexture);
            }
            catch (Exception e)
            {
                if (replacement)
                    drawSurface!.Dispose();
                _lost = true;
                throw new RenderTargetCorruptedException(e);
            }

            needsEndDraw = true;
            var offset = new PixelPoint(off.X, off.Y);
            using var texture = MicroComRuntime.CreateProxyFor<IUnknown>(pTexture, true);

            var session = new Session(this, drawSurface!, replacement, texture, transaction, size, offset, scale);
            transaction = null;
            return session;
        }
        finally
        {
            if (transaction != null)
            {
                if (needsEndDraw)
                    drawSurface!.Surface.EndDraw();
                transaction.Dispose();
            }
        }
    }

    private void PublishSurface(SurfaceSet replacement)
    {
        var previous = _activeSurface;
        _activeSurface = replacement;
        _window.SetSurface(replacement.Surface);
        previous?.Dispose();
    }

    private class Session : IDirect3D11TextureRenderTargetRenderSession
    {
        private readonly DirectCompositedWindowRenderTarget _owner;
        private readonly SurfaceSet _surface;
        private readonly bool _publishSurface;
        private readonly IDisposable _transaction;
        private readonly PixelSize _size;
        private readonly PixelPoint _offset;
        private readonly double _scaling;
        private readonly IUnknown _texture;

        public Session(DirectCompositedWindowRenderTarget owner, SurfaceSet surface, bool publishSurface, IUnknown texture, IDisposable transaction,
            PixelSize size, PixelPoint offset, double scaling)
        {
            _owner = owner;
            _surface = surface;
            _publishSurface = publishSurface;
            _transaction = transaction;
            _size = size;
            _offset = offset;
            _scaling = scaling;
            _texture = texture.CloneReference();
        }

        public void Dispose()
        {
            try
            {
                _texture.Dispose();
                _surface.Surface.EndDraw();
                if (_publishSurface)
                    _owner.PublishSurface(_surface);
            }
            catch
            {
                if (_publishSurface)
                    _surface.Dispose();
                throw;
            }
            finally
            {
                _transaction.Dispose();
            }
        }

        public IntPtr D3D11Texture2D => _texture.GetNativeIntPtr();
        public PixelSize Size => _size;
        public PixelPoint Offset => _offset;
        public double Scaling => _scaling;
    }
}
