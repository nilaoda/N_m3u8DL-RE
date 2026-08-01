window.nm3u8dlWeb = window.nm3u8dlWeb || {};

window.nm3u8dlWeb.toolchainVersion = {
  ffmpeg: '0.12.10',
  ffmpegUtil: '0.12.10',
  ffmpegCore: '0.12.10',
};

window.nm3u8dlWeb.getToolchainSummary = function () {
  return [
    'ffmpeg.wasm: supported through pinned CDN URLs with a browser-side WASM runtime.',
    'bento4: not available as a browser-native WASM runtime in this HTML shell; it remains a desktop/native integration target.',
    'Shaka Packager: not available as a browser-native WASM runtime in this HTML shell; it remains a desktop/native integration target.',
    'mp4decrypt: not available as a browser-native WASM runtime in this HTML shell; it remains a desktop/native integration target.',
    'Update compatibility: browser integration uses pinned versioned library URLs to avoid silent breakage from upstream CDN changes.'
  ];
};

window.nm3u8dlWeb.loadFfmpegWasm = async function () {
  if (window.nm3u8dlWeb.ffmpegLoaded) {
    return window.nm3u8dlWeb.ffmpegLoaded;
  }

  const module = await import(`https://cdn.jsdelivr.net/npm/@ffmpeg/ffmpeg@${window.nm3u8dlWeb.toolchainVersion.ffmpeg}/dist/esm/index.js`);
  const utils = await import(`https://cdn.jsdelivr.net/npm/@ffmpeg/util@${window.nm3u8dlWeb.toolchainVersion.ffmpegUtil}/dist/esm/index.js`);

  const ffmpeg = await module.ffmpeg;
  const fetchFile = utils.fetchFile;
  const toBlobURL = utils.toBlobURL;

  const baseURL = `https://cdn.jsdelivr.net/npm/@ffmpeg/core@${window.nm3u8dlWeb.toolchainVersion.ffmpegCore}/dist/esm`;
  await ffmpeg.load({
    coreURL: await toBlobURL(`${baseURL}/ffmpeg-core.js`, 'text/javascript'),
    wasmURL: await toBlobURL(`${baseURL}/ffmpeg-core.wasm`, 'application/wasm')
  });

  window.nm3u8dlWeb.ffmpegLoaded = { ffmpeg, fetchFile };
  return window.nm3u8dlWeb.ffmpegLoaded;
};

window.nm3u8dlWeb.runFfmpegCommand = async function (command, files) {
  const { ffmpeg, fetchFile } = await window.nm3u8dlWeb.loadFfmpegWasm();
  const input = files[0];

  await ffmpeg.writeFile(input.name, await fetchFile(input));
  await ffmpeg.exec(command);

  const output = await ffmpeg.readFile(command[command.length - 1]);

  return {
    output: output instanceof Uint8Array ? new Blob([output]) : output,
    stdout: '',
    stderr: ''
  };
};
