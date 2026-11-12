import { dotnet } from './_framework/dotnet.js';

const status = document.getElementById('status');
const canvas = document.getElementById('canvas');

async function loadGameConfig() {
  const response = await fetch('./game/webgl/game.json');
  if (!response.ok) {
    throw new Error(`Failed to load game.json: ${response.status} ${response.statusText}`);
  }

  const config = await response.json();
  if (!config || typeof config !== 'object') {
    throw new Error('game.json must contain a JSON object');
  }

  if (
    !Number.isInteger(config.virtualWidth) ||
    !Number.isInteger(config.virtualHeight) ||
    config.virtualWidth <= 0 ||
    config.virtualHeight <= 0
  ) {
    throw new Error('game.json must define positive integer virtualWidth and virtualHeight values');
  }

  return config;
}

function prepareCanvas(virtualWidth, virtualHeight) {
  const widthLimit = `${(virtualWidth / virtualHeight) * 100}vh`;
  const heightLimit = `${(virtualHeight / virtualWidth) * 100}vw`;

  canvas.width = virtualWidth;
  canvas.height = virtualHeight;
  canvas.widthNative = virtualWidth;
  canvas.heightNative = virtualHeight;

  canvas.style.setProperty('--canvas-width-limit', widthLimit);
  canvas.style.setProperty('--canvas-height-limit', heightLimit);
  restoreCanvasCss(widthLimit, heightLimit);
}

function restoreCanvasCss(
  widthLimit = canvas.style.getPropertyValue('--canvas-width-limit'),
  heightLimit = canvas.style.getPropertyValue('--canvas-height-limit')
) {
  if (!widthLimit || !heightLimit) {
    return;
  }

  const width = `min(100vw, ${widthLimit})`;
  const height = `min(100vh, ${heightLimit})`;
  const needRestore =
    canvas.style.getPropertyValue('max-width') !== '100vw' ||
    canvas.style.getPropertyValue('max-height') !== '100vh' ||
    canvas.style.getPropertyValue('width') !== width ||
    canvas.style.getPropertyValue('height') !== height;

  if (!needRestore) {
    return;
  }

  canvas.style.setProperty('max-width', '100vw', 'important');
  canvas.style.setProperty('max-height', '100vh', 'important');
  canvas.style.setProperty('width', width, 'important');
  canvas.style.setProperty('height', height, 'important');
}

window.addEventListener('resize', restoreCanvasCss);
window.addEventListener('orientationchange', () => {
  restoreCanvasCss();
  window.setTimeout(restoreCanvasCss, 250);
});

try {
  const gameConfig = await loadGameConfig();
  prepareCanvas(gameConfig.virtualWidth, gameConfig.virtualHeight);

  const { getAssemblyExports, runMain } = await dotnet
    .withDiagnosticTracing(false)
    .create();

  dotnet.instance.Module.canvas = canvas;
  await runMain();

  const exports = await getAssemblyExports('YesodScript.Runtime.Raylib.Web.dll');
  const program = exports.YesodScript.Runtime.Raylib.Web.BrowserHost;
  restoreCanvasCss();
  status.hidden = true;

  const resizeRaylibWindowToCanvas = (forceRefresh = false) => {
    const bounds = canvas.getBoundingClientRect();
    const width = Math.round(bounds.width);
    const height = Math.round(bounds.height);
    if (width > 0 && height > 0) {
      if (forceRefresh) {
        program.ResizeWindow(width + 1, height);
      }
      program.ResizeWindow(width, height);
    }
  };

  let resizeFrame = 0;
  const resizeObserver = new ResizeObserver(() => {
    cancelAnimationFrame(resizeFrame);
    resizeFrame = requestAnimationFrame(() => {
      resizeRaylibWindowToCanvas();
    });
  });
  resizeObserver.observe(canvas);
  resizeRaylibWindowToCanvas(true);

  const cssGuard = new MutationObserver(() => restoreCanvasCss());
  cssGuard.observe(canvas, { attributes: true, attributeFilter: ['style'] });

  function loop() {
    if (program.UpdateFrame()) {
      const fatalError = program.GetFatalErrorText();
      if (fatalError) {
        status.textContent = fatalError;
        status.hidden = false;
      }
      requestAnimationFrame(loop);
    }
  }
  requestAnimationFrame(loop);

  window.addEventListener('pagehide', () => program.Cleanup(), { once: true });
} catch (err) {
  status.textContent = 'Failed: ' + (err && err.message ? err.message : err);
  status.hidden = false;
  console.error(err);
}
