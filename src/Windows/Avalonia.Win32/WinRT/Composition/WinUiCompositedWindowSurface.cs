using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.OpenGL.Egl;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Win32.DirectX;
using Avalonia.Win32.Interop;
using MicroCom.Runtime;

namespace Avalonia.Win32.WinRT.Composition
{
    internal class WinUiCompositedWindowSurface : IDirect3D11TexturePlatformSurface, IDirect3D11TexturePlatformSurface2, IDisposable, ICompositionEffectsSurface
    {
        private readonly WinUiCompositionShared _shared;
        private readonly EglGlPlatformSurface.IEglWindowGlPlatformSurfaceInfo _info;
        private WinUiCompositedWindow? _window;
        private BlurEffect _blurEffect;

        public WinUiCompositedWindowSurface(WinUiCompositionShared shared, EglGlPlatformSurface.IEglWindowGlPlatformSurfaceInfo info)
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
            var cornerRadius = AvaloniaLocator.Current.GetService<Win32PlatformOptions>()
                ?.WinUICompositionBackdropCornerRadius;
            _window ??= new WinUiCompositedWindow(_info, _shared, cornerRadius);
            _window.SetBlur(_blurEffect);

            return new WinUiCompositedWindowRenderTarget(context, _window, d3dDevice, _shared.Compositor);
        }

        public void Dispose()
        {
            _window?.Dispose();
            _window = null;
        }

        public bool IsBlurSupported(BlurEffect effect) => effect switch
        {
            BlurEffect.None => true,
            BlurEffect.Acrylic => Win32Platform.WindowsVersion >= WinUiCompositionShared.MinAcrylicVersion,
            BlurEffect.MicaLight => Win32Platform.WindowsVersion >= WinUiCompositionShared.MinHostBackdropVersion,
            BlurEffect.MicaDark => Win32Platform.WindowsVersion >= WinUiCompositionShared.MinHostBackdropVersion,
            _ => false
        };

        public void SetBlur(BlurEffect enable)
        {
            _blurEffect = enable;
            _window?.SetBlur(enable);
        }
    }

    internal class WinUiCompositedWindowRenderTarget : IDirect3D11TextureRenderTarget, IDirect3D11TextureRenderTarget2
    {
        private static readonly Guid IID_ID3D11Texture2D = Guid.Parse("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        private readonly IPlatformGraphicsContext _context;
        private readonly WinUiCompositedWindow _window;
        private readonly IUnknown _d3dDevice;
        private readonly ICompositor _compositor;
        private readonly ICompositorInterop _interop;
        private readonly ICompositionGraphicsDevice _compositionDevice;
        private readonly ICompositionGraphicsDevice2 _compositionDevice2;
        private SurfaceSet? _activeSurface;
        private bool _lost;
        private sealed class SurfaceSet : IDisposable
        {
            public readonly ICompositionDrawingSurface DrawingSurface;
            public readonly ICompositionSurface Surface;
            public readonly ICompositionDrawingSurfaceInterop Interop;
            public readonly PixelSize Size;
            public readonly bool SupportsTransparency;

            public SurfaceSet(ICompositionDrawingSurface drawingSurface, ICompositionSurface surface,
                ICompositionDrawingSurfaceInterop interop, PixelSize size, bool supportsTransparency)
            {
                DrawingSurface = drawingSurface;
                Surface = surface;
                Interop = interop;
                Size = size;
                SupportsTransparency = supportsTransparency;
            }

            public void Dispose()
            {
                Surface.Dispose();
                Interop.Dispose();
                DrawingSurface.Dispose();
            }
        }

        public WinUiCompositedWindowRenderTarget(IPlatformGraphicsContext context,
            WinUiCompositedWindow window, IntPtr device,
            ICompositor compositor)
        {
            _context = context;
            _window = window;

            try
            {
                _d3dDevice = MicroComRuntime.CreateProxyFor<IUnknown>(device, false).CloneReference();
                _compositor = compositor.CloneReference();
                _interop = compositor.QueryInterface<ICompositorInterop>();
                _compositionDevice = _interop.CreateGraphicsDevice(_d3dDevice);
                _compositionDevice2 = _compositionDevice.QueryInterface<ICompositionGraphicsDevice2>();
            }
            catch
            {
                _compositionDevice2?.Dispose();
                _compositionDevice?.Dispose();
                _interop?.Dispose();
                _compositor?.Dispose();
                _d3dDevice?.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _activeSurface?.Dispose();
            _compositionDevice2.Dispose();
            _compositionDevice.Dispose();
            _interop.Dispose();
            _compositor.Dispose();
            _d3dDevice.Dispose();
        }

        private SurfaceSet CreateSurface(PixelSize capacity, bool isTransparency)
        {
            // Do not use Premultiplied when the window is not Transparency. Because the Premultiplied AlphaMode will increase the performance loss of DWM. See https://github.com/AvaloniaUI/Avalonia/issues/20643
            var alphaMode = isTransparency ? DirectXAlphaMode.Premultiplied : DirectXAlphaMode.Ignore;
            var drawingSurface = _compositionDevice2.CreateDrawingSurface2(new UnmanagedMethods.SIZE()
                {
                    X = capacity.Width,
                    Y = capacity.Height,
                },
                DirectXPixelFormat.B8G8R8A8UIntNormalized, alphaMode);
            try
            {
                var surface = drawingSurface.QueryInterface<ICompositionSurface>();
                var interop = drawingSurface.QueryInterface<ICompositionDrawingSurfaceInterop>();
                return new SurfaceSet(drawingSurface, surface, interop, capacity, isTransparency);
            }
            catch
            {
                drawingSurface.Dispose();
                throw;
            }
        }

        public PlatformRenderTargetState State =>
            _context.IsLost || _lost ? PlatformRenderTargetState.Corrupted : PlatformRenderTargetState.Ready;

        IDirect3D11TextureRenderTargetRenderSession IDirect3D11TextureRenderTarget.BeginDraw()
        {
            var fallbackSceneInfo = new IRenderTarget.RenderTargetSceneInfo(_window.WindowInfo.Size,
                _window.WindowInfo.Scaling, CompositionTransparencyLevel.None);
            return BeginDraw(fallbackSceneInfo);
        }

        public unsafe IDirect3D11TextureRenderTargetRenderSession BeginDraw(
            IRenderTarget.RenderTargetSceneInfo sceneInfo)
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
                var capacity = CompositionSurfaceAllocationPolicy.GetCapacity(size, previousSurface?.Size);
                var replacement = previousSurface is null || !CompositionSurfaceAllocationPolicy.Fits(size, previousSurface.Size) ||
                                  previousSurface.SupportsTransparency != isTransparency;
                drawSurface = replacement ? CreateSurface(capacity, isTransparency) : previousSurface;
                
                void* pTexture;
                UnmanagedMethods.POINT off;
                try
                {
                    var iid = IID_ID3D11Texture2D;
                    off = drawSurface!.Interop.BeginDraw(null, &iid, &pTexture);
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
                        drawSurface!.Interop.EndDraw();
                    transaction.Dispose();
                }
            }
        }

        private void PublishSurface(SurfaceSet replacement, PixelSize size)
        {
            var previous = _activeSurface;
            _activeSurface = replacement;
            _window.ResizeIfNeeded(size);
            _window.SetSurface(replacement.Surface);
            previous?.Dispose();
        }

        private void ResizeVisual(PixelSize size) => _window.ResizeIfNeeded(size);

        private class Session : IDirect3D11TextureRenderTargetRenderSession
        {
            private readonly WinUiCompositedWindowRenderTarget _owner;
            private readonly SurfaceSet _surface;
            private readonly bool _publishSurface;
            private readonly IDisposable _transaction;
            private readonly PixelSize _size;
            private readonly PixelPoint _offset;
            private readonly double _scaling;
            private readonly IUnknown _texture;

            public Session(WinUiCompositedWindowRenderTarget owner, SurfaceSet surface, bool publishSurface, IUnknown texture, IDisposable transaction,
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
                    _surface.Interop.EndDraw();
                    if (_publishSurface)
                        _owner.PublishSurface(_surface, _size);
                    else
                        _owner.ResizeVisual(_size);
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
}
