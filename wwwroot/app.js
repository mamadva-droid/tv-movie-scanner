// TV Movie Scanner - Main Application Logic

let currentFacingMode = 'environment';
let cameraStream = null;
let currentMode = 'camera'; // 'camera' or 'mic'
let isScanning = false;
let isLiveScanning = false;
let liveScanInterval = null;
let audioRecorder = null;
let audioChunks = [];
let currentAbortController = null;

// DOM Elements
const videoEl = document.getElementById('camera-stream');
const canvasEl = document.getElementById('capture-canvas');
const scanLaserEl = document.getElementById('scan-laser');
const scannerHintEl = document.getElementById('scanner-hint');
const btnScan = document.getElementById('btn-scan');
const shutterIcon = document.getElementById('shutter-icon');
const btnSwitchCamera = document.getElementById('btn-switch-camera');
const btnGallery = document.getElementById('btn-gallery');
const filePicker = document.getElementById('file-picker');
const tabCamera = document.getElementById('tab-camera');
const tabMic = document.getElementById('tab-mic');
const audioOverlay = document.getElementById('audio-overlay');
const audioTimer = document.getElementById('audio-timer');
const btnLiveToggle = document.getElementById('btn-live-toggle');

// Full-screen Scanning Overlay Elements
const scanningOverlay = document.getElementById('scanning-overlay');
const scanningPreviewImg = document.getElementById('scanning-preview-img');
const scanStepTitle = document.getElementById('scan-step-title');
const scanStepDesc = document.getElementById('scan-step-desc');
const step1 = document.getElementById('step-1');
const step2 = document.getElementById('step-2');
const step3 = document.getElementById('step-3');
const btnCancelScan = document.getElementById('btn-cancel-scan');

// Result Sheet Elements
const resultSheet = document.getElementById('result-sheet');
const btnCloseSheet = document.getElementById('btn-close-sheet');
const btnScanAgain = document.getElementById('btn-scan-again');
const btnWatchTrailer = document.getElementById('btn-watch-trailer');
const resultPoster = document.getElementById('result-poster');
const resultBackdrop = document.getElementById('result-backdrop');
const posterFallbackCard = document.getElementById('poster-fallback-card');
const posterFallbackTitle = document.getElementById('poster-fallback-title');
const resultType = document.getElementById('result-type');
const resultTitle = document.getElementById('result-title');
const resultOriginalTitle = document.getElementById('result-original-title');
const resultDirectorMeta = document.getElementById('result-director-meta');
const resultDurationMeta = document.getElementById('result-duration-meta');
const resultRating = document.getElementById('result-rating');
const resultKpRating = document.getElementById('result-kp-rating');
const resultYear = document.getElementById('result-year');
const resultAge = document.getElementById('result-age');
const resultConfidence = document.getElementById('result-confidence');
const resultAiExplanation = document.getElementById('result-ai-explanation');
const resultGenres = document.getElementById('result-genres');
const resultOverview = document.getElementById('result-overview');
const resultCast = document.getElementById('result-cast');
const castSection = document.getElementById('cast-section');
const resultFacts = document.getElementById('result-facts');
const factsSection = document.getElementById('facts-section');
const resultWatchPlatforms = document.getElementById('result-watch-platforms');
const watchSection = document.getElementById('watch-section');

// Actor Modal Elements
const modalActor = document.getElementById('modal-actor');
const btnCloseActor = document.getElementById('btn-close-actor');
const actorModalName = document.getElementById('actor-modal-name');
const actorModalRole = document.getElementById('actor-modal-role');
const actorModalBio = document.getElementById('actor-modal-bio');
const btnActorKp = document.getElementById('btn-actor-kp');
const btnActorGoogle = document.getElementById('btn-actor-google');

// Modals
const modalSettings = document.getElementById('modal-settings');
const btnSettings = document.getElementById('btn-settings');
const btnCloseSettings = document.getElementById('btn-close-settings');
const inputGeminiKey = document.getElementById('input-gemini-key');
const inputTmdbKey = document.getElementById('input-tmdb-key');
const btnSaveSettings = document.getElementById('btn-save-settings');

const modalPhone = document.getElementById('modal-phone');
const btnPhoneConnect = document.getElementById('btn-phone-connect');
const btnClosePhone = document.getElementById('btn-close-phone');
const phoneConnectUrl = document.getElementById('phone-connect-url');
const btnCopyUrl = document.getElementById('btn-copy-url');
const nativeCameraInput = document.getElementById('native-camera-input');

// Search Bar Elements
const inputMovieSearch = document.getElementById('input-movie-search');
const btnSearchGo = document.getElementById('btn-search-go');
const btnClearSearch = document.getElementById('btn-clear-search');

// Toast Notification
function showToast(message, duration = 3000) {
  const toast = document.getElementById('toast');
  if (!toast) return;
  toast.textContent = message;
  toast.classList.add('show');
  setTimeout(() => {
    toast.classList.remove('show');
  }, duration);
}

// Initialize Application
document.addEventListener('DOMContentLoaded', () => {
  initEventListeners();

  if (window.lucide) {
    try { lucide.createIcons(); } catch (e) { console.warn('Lucide icon init error:', e); }
  }

  // Register PWA Service Worker
  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('/sw.js').catch(err => {
      console.log('SW registration error:', err);
    });
  }

  loadSavedKeys();
  initCamera();
  loadServerInfo();
});

// Load API Keys from LocalStorage & Server Sync
async function loadSavedKeys() {
  let gemini = localStorage.getItem('gemini_api_key') || '';
  let tmdb = localStorage.getItem('tmdb_api_key') || '';
  if (inputGeminiKey) inputGeminiKey.value = gemini;
  if (inputTmdbKey) inputTmdbKey.value = tmdb;

  try {
    const res = await fetch('/api/keys');
    if (res.ok) {
      const data = await res.json();
      if (data.geminiApiKey && !gemini) {
        gemini = data.geminiApiKey;
        if (inputGeminiKey) inputGeminiKey.value = gemini;
        localStorage.setItem('gemini_api_key', gemini);
      }
      if (data.tmdbApiKey && !tmdb) {
        tmdb = data.tmdbApiKey;
        if (inputTmdbKey) inputTmdbKey.value = tmdb;
        localStorage.setItem('tmdb_api_key', tmdb);
      }
    }
  } catch (e) {
    console.log('Error syncing keys with server:', e);
  }
}

// Camera Management
async function initCamera() {
  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    return;
  }

  try {
    if (cameraStream) {
      cameraStream.getTracks().forEach(track => track.stop());
    }

    const constraints = {
      video: {
        facingMode: currentFacingMode,
        width: { ideal: 1920 },
        height: { ideal: 1080 }
      },
      audio: false
    };

    cameraStream = await navigator.mediaDevices.getUserMedia(constraints);
    videoEl.srcObject = cameraStream;
    await videoEl.play();
  } catch (err) {
    console.warn('Live camera stream not available (requires HTTPS on mobile):', err);
  }
}

function switchCamera() {
  currentFacingMode = currentFacingMode === 'environment' ? 'user' : 'environment';
  initCamera();
  showToast(currentFacingMode === 'environment' ? 'Задняя камера' : 'Фронтальная камера');
}

// Helper: Scale and optimize full frame so AI can detect TV anywhere in the room shot
async function cropAndOptimizeTVFrame(input) {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => {
      const srcW = img.naturalWidth || img.width;
      const srcH = img.naturalHeight || img.height;

      // Scale proportionally up to max 1280px preserving the entire photo
      const maxDim = 1280;
      let outW = srcW;
      let outH = srcH;

      if (srcW > maxDim || srcH > maxDim) {
        if (srcW >= srcH) {
          outW = maxDim;
          outH = Math.round((srcH * maxDim) / srcW);
        } else {
          outH = maxDim;
          outW = Math.round((srcW * maxDim) / srcH);
        }
      }

      const offCanvas = document.createElement('canvas');
      offCanvas.width = outW;
      offCanvas.height = outH;
      const ctx = offCanvas.getContext('2d');

      // Draw the complete image without blind cropping
      ctx.drawImage(img, 0, 0, srcW, srcH, 0, 0, outW, outH);

      const optimizedBase64 = offCanvas.toDataURL('image/jpeg', 0.85);
      resolve(optimizedBase64);
    };
    img.onerror = reject;

    if (typeof input === 'string') {
      img.src = input;
    } else if (input instanceof HTMLVideoElement) {
      const tempCanvas = document.createElement('canvas');
      tempCanvas.width = input.videoWidth;
      tempCanvas.height = input.videoHeight;
      const tCtx = tempCanvas.getContext('2d');
      tCtx.drawImage(input, 0, 0);
      img.src = tempCanvas.toDataURL('image/jpeg', 0.9);
    }
  });
}

// Event Listeners
function initEventListeners() {
  if (btnSwitchCamera) btnSwitchCamera.addEventListener('click', switchCamera);

  // Gallery Picker
  if (btnGallery) btnGallery.addEventListener('click', () => filePicker.click());
  if (filePicker) filePicker.addEventListener('change', handleFilePick);

  // Native Direct Camera Input
  if (nativeCameraInput) {
    nativeCameraInput.addEventListener('change', (e) => {
      if (!isScanning) {
        handleFilePick(e);
      }
      setTimeout(() => { e.target.value = ''; }, 500);
    });
  }

  // Smart Search Bar
  if (inputMovieSearch) {
    inputMovieSearch.addEventListener('input', () => {
      if (btnClearSearch) {
        btnClearSearch.style.display = inputMovieSearch.value.trim().length > 0 ? 'flex' : 'none';
      }
    });

    inputMovieSearch.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        inputMovieSearch.blur();
        executeTextSearch(inputMovieSearch.value.trim());
      }
    });
  }

  if (btnClearSearch && inputMovieSearch) {
    btnClearSearch.addEventListener('click', () => {
      inputMovieSearch.value = '';
      btnClearSearch.style.display = 'none';
      inputMovieSearch.focus();
    });
  }

  if (btnSearchGo && inputMovieSearch) {
    btnSearchGo.addEventListener('click', () => {
      inputMovieSearch.blur();
      executeTextSearch(inputMovieSearch.value.trim());
    });
  }

  // Live Stream Toggle
  if (btnLiveToggle) {
    btnLiveToggle.addEventListener('click', toggleLiveScan);
  }

  // Cancel Scan Button
  if (btnCancelScan) {
    btnCancelScan.addEventListener('click', () => {
      if (currentAbortController) {
        currentAbortController.abort();
      }
      isScanning = false;
      setScanningUI(false);
      showToast('Сканирование отменено');
    });
  }

  // Mode Tabs
  if (tabCamera) tabCamera.addEventListener('click', () => setMode('camera'));
  if (tabMic) tabMic.addEventListener('click', () => setMode('mic'));

  // Main Scan Trigger
  if (btnScan) {
    btnScan.addEventListener('click', (e) => {
      if (isScanning) {
        e.preventDefault();
        return;
      }
      if (currentMode === 'camera') {
        if (videoEl && videoEl.videoWidth > 0 && !videoEl.paused) {
          e.preventDefault();
          captureAndRecognize();
        }
        // When video is not playing, default label action opens nativeCameraInput instantly
      } else {
        e.preventDefault();
        startAudioScan();
      }
    });
  }

  // Actor Modal
  if (btnCloseActor && modalActor) {
    btnCloseActor.addEventListener('click', () => modalActor.classList.remove('open'));
  }

  // Settings Modal
  if (btnSettings && modalSettings) {
    btnSettings.addEventListener('click', () => modalSettings.classList.add('open'));
  }
  if (btnCloseSettings && modalSettings) {
    btnCloseSettings.addEventListener('click', () => modalSettings.classList.remove('open'));
  }
  if (modalSettings) {
    modalSettings.addEventListener('click', (e) => {
      if (e.target === modalSettings) modalSettings.classList.remove('open');
    });
  }
  if (btnSaveSettings) {
    btnSaveSettings.addEventListener('click', async () => {
      const geminiKey = inputGeminiKey.value.trim();
      const tmdbKey = inputTmdbKey.value.trim();

      localStorage.setItem('gemini_api_key', geminiKey);
      localStorage.setItem('tmdb_api_key', tmdbKey);

      try {
        await fetch('/api/save-keys', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            geminiApiKey: geminiKey,
            tmdbApiKey: tmdbKey
          })
        });
      } catch (e) {
        console.warn('Could not save keys to server:', e);
      }

      if (modalSettings) modalSettings.classList.remove('open');
      showToast('Настройки сохранены и синхронизированы!');
    });
  }

  // Phone Connect Modal
  if (btnPhoneConnect && modalPhone) {
    btnPhoneConnect.addEventListener('click', () => modalPhone.classList.add('open'));
  }
  if (btnClosePhone && modalPhone) {
    btnClosePhone.addEventListener('click', () => modalPhone.classList.remove('open'));
  }
  if (modalPhone) {
    modalPhone.addEventListener('click', (e) => {
      if (e.target === modalPhone) modalPhone.classList.remove('open');
    });
  }
  if (modalActor) {
    modalActor.addEventListener('click', (e) => {
      if (e.target === modalActor) modalActor.classList.remove('open');
    });
  }
  if (btnCopyUrl && phoneConnectUrl) {
    btnCopyUrl.addEventListener('click', () => {
      navigator.clipboard.writeText(phoneConnectUrl.textContent);
      showToast('Ссылка скопирована!');
    });
  }

  // Escape key closes modals
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
      if (modalSettings) modalSettings.classList.remove('open');
      if (modalPhone) modalPhone.classList.remove('open');
      if (modalActor) modalActor.classList.remove('open');
      closeResultSheet();
    }
  });

  // Result Sheet
  if (btnCloseSheet) btnCloseSheet.addEventListener('click', closeResultSheet);
  if (btnScanAgain) btnScanAgain.addEventListener('click', closeResultSheet);
}

function setMode(mode) {
  currentMode = mode;
  if (mode === 'camera') {
    if (tabCamera) tabCamera.classList.add('active');
    if (tabMic) tabMic.classList.remove('active');
    if (audioOverlay) audioOverlay.style.display = 'none';
    if (scannerHintEl) {
      scannerHintEl.innerHTML = '<i data-lucide="scan" class="hint-icon"></i> <span>Наведите камеру на экран ТВ</span>';
    }
  } else {
    if (tabMic) tabMic.classList.add('active');
    if (tabCamera) tabCamera.classList.remove('active');
    if (scannerHintEl) {
      scannerHintEl.innerHTML = '<i data-lucide="mic" class="hint-icon"></i> <span>Нажмите кнопку для записи звука</span>';
    }
  }
  if (window.lucide) lucide.createIcons();
}

// Live Stream Continuous Scanning
function toggleLiveScan() {
  if (!videoEl || !videoEl.videoWidth || videoEl.paused) {
    showToast('Для Live-сканера откройте https://192.168.0.120:5001');
    return;
  }

  isLiveScanning = !isLiveScanning;
  if (isLiveScanning) {
    if (btnLiveToggle) btnLiveToggle.classList.add('active');
    showToast('Live-сканер включен: распознавание каждые 4 сек');
    liveScanInterval = setInterval(() => {
      if (!isScanning && resultSheet && !resultSheet.classList.contains('open') && videoEl.videoWidth > 0) {
        captureAndRecognize(null, true);
      }
    }, 4000);
  } else {
    if (btnLiveToggle) btnLiveToggle.classList.remove('active');
    clearInterval(liveScanInterval);
    liveScanInterval = null;
    showToast('Live-сканер выключен');
  }
}

// Full-screen Scanning Overlay Management
function setScanningUI(active, imageBase64 = null, stepTitle = '', stepDesc = '', currentStep = 2) {
  if (!scanningOverlay) return;
  if (active) {
    if (imageBase64 && scanningPreviewImg) {
      scanningPreviewImg.src = imageBase64;
    }
    if (scanStepTitle) scanStepTitle.textContent = stepTitle || 'Анализируем экран телевизора...';
    if (scanStepDesc) scanStepDesc.textContent = stepDesc || 'Кадр оптимизирован под формат 16:9';
    
    if (step1) step1.className = 'step-item active';
    if (step2) step2.className = currentStep >= 2 ? 'step-item active' : 'step-item';
    if (step3) step3.className = currentStep >= 3 ? 'step-item active' : 'step-item';

    scanningOverlay.classList.add('active');
  } else {
    scanningOverlay.classList.remove('active');
  }
  if (window.lucide) lucide.createIcons();
}

// Execute Smart Text Search
async function executeTextSearch(query) {
  if (!query) {
    showToast('Введите название фильма или сериала');
    return;
  }
  if (isScanning) return;
  isScanning = true;
  currentAbortController = new AbortController();

  try {
    setScanningUI(true, null, `Ищем: ${query}`, 'Поиск фильма, постеров и актеров в кино-базе...', 2);

    const geminiKey = localStorage.getItem('gemini_api_key') || '';
    // Call text search endpoint
    const response = await fetch(`/api/search-title?query=${encodeURIComponent(query)}&clientApiKey=${encodeURIComponent(geminiKey)}`, {
      signal: currentAbortController.signal
    });

    if (!response.ok) {
      throw new Error('Не удалось найти фильм по запросу');
    }

    const geminiResult = await response.json();
    setScanningUI(true, null, `Найдено: ${geminiResult.title || query}`, 'Загрузка официального постера и фото актеров...', 3);

    // Query Kinopoisk / TMDB for HD poster and HD actor photos
    let tmdbData = null;
    try {
      const tmdbKey = localStorage.getItem('tmdb_api_key') || '';
      const searchUrl = `/api/tmdb-search?query=${encodeURIComponent(geminiResult.title || query)}&year=${geminiResult.releaseYear || ''}&clientApiKey=${encodeURIComponent(tmdbKey)}`;
      const tmdbRes = await fetch(searchUrl, { signal: currentAbortController.signal });
      if (tmdbRes.ok) {
        tmdbData = await tmdbRes.json();
      }
    } catch (e) {
      console.warn('Metadata fetch error:', e);
    }

    displayResult(geminiResult, tmdbData, null);
  } catch (err) {
    if (err.name === 'AbortError') {
      showToast('Поиск отменен');
    } else {
      console.error('Search error:', err);
      showToast(err.message || 'Ошибка поиска. Попробуйте еще раз.');
    }
  } finally {
    isScanning = false;
    currentAbortController = null;
    setScanningUI(false);
  }
}

// Capture Video Frame and Run Recognition
async function captureAndRecognize(overrideRaw = null, isLiveBackground = false) {
  if (isScanning) return;

  let rawSource = overrideRaw;

  if (!rawSource) {
    if (!videoEl || !videoEl.videoWidth || !videoEl.videoHeight) {
      if (nativeCameraInput) {
        nativeCameraInput.click();
        return;
      }
      showToast('Видеопоток камеры еще не готов');
      return;
    }
    rawSource = videoEl;
  }

  isScanning = true;
  currentAbortController = new AbortController();

  try {
    // Step 1: Auto-Crop to 16:9 TV frame and downscale to ~80KB
    const optimizedBase64 = await cropAndOptimizeTVFrame(rawSource);

    if (!isLiveBackground) {
      setScanningUI(true, optimizedBase64, 'Анализируем экран телевизора...', 'Нейросеть Gemini 2.5 распознает актеров, сцену и маркеры фильма', 2);
    }

    const geminiKey = localStorage.getItem('gemini_api_key') || '';
    const tmdbKey = localStorage.getItem('tmdb_api_key') || '';

    // Step 2: Send frame to Gemini Vision API with 45s timeout
    const timeoutId = setTimeout(() => {
      if (currentAbortController) currentAbortController.abort();
    }, 45000);

    const geminiResponse = await fetch('/api/recognize-image', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        imageBase64: optimizedBase64,
        apiKey: geminiKey
      }),
      signal: currentAbortController.signal
    });

    clearTimeout(timeoutId);

    const geminiResult = await geminiResponse.json();

    if (!geminiResponse.ok) {
      throw new Error(geminiResult.error || 'Ошибка при распознавании кадра');
    }

    if (!isLiveBackground) {
      setScanningUI(true, optimizedBase64, `Найдено: ${geminiResult.title}`, 'Загрузка постера, сюжета и кино-энциклопедии...', 3);
    }

    // Step 3: Query TMDB for rich poster and backdrops
    let tmdbData = null;
    try {
      const searchUrl = `/api/tmdb-search?query=${encodeURIComponent(geminiResult.title)}&year=${geminiResult.releaseYear || ''}&clientApiKey=${encodeURIComponent(tmdbKey)}`;
      const tmdbResponse = await fetch(searchUrl, { signal: currentAbortController.signal });
      if (tmdbResponse.ok) {
        tmdbData = await tmdbResponse.json();
      }
    } catch (e) {
      console.warn('TMDB fetch error:', e);
    }

    displayResult(geminiResult, tmdbData, optimizedBase64);
  } catch (err) {
    if (err.name === 'AbortError') {
      console.log('Scan aborted by user or timeout.');
      showToast('Время ожидания ответа истекло. Попробуйте еще раз.');
    } else {
      console.error('Scan failed:', err);
      showToast(err.message || 'Не удалось распознать фильм. Попробуйте еще раз.', 4000);
    }
  } finally {
    isScanning = false;
    currentAbortController = null;
    setScanningUI(false);
  }
}

// Audio Recording Scan
async function startAudioScan() {
  if (isScanning) return;

  try {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    if (audioOverlay) audioOverlay.style.display = 'flex';

    let secondsLeft = 5;
    if (audioTimer) audioTimer.textContent = `${secondsLeft} сек`;
    
    const interval = setInterval(() => {
      secondsLeft--;
      if (secondsLeft > 0) {
        if (audioTimer) audioTimer.textContent = `${secondsLeft} сек`;
      } else {
        clearInterval(interval);
      }
    }, 1000);

    audioRecorder = new MediaRecorder(stream);
    audioChunks = [];
    audioRecorder.ondataavailable = (e) => audioChunks.push(e.data);
    audioRecorder.onstop = async () => {
      stream.getTracks().forEach(t => t.stop());
      if (audioOverlay) audioOverlay.style.display = 'none';
      showToast('Аудио записано. Для максимально точного определения сфотографируйте экран ТВ.');
    };

    audioRecorder.start();
    setTimeout(() => {
      if (audioRecorder && audioRecorder.state === 'recording') {
        audioRecorder.stop();
      }
    }, 5000);

  } catch (err) {
    showToast('Микрофон недоступен: ' + err.message);
    if (audioOverlay) audioOverlay.style.display = 'none';
  }
}

// Handle File Pick from Gallery / Native Camera
function handleFilePick(e) {
  const file = e.target.files[0];
  if (!file) return;

  const reader = new FileReader();
  reader.onload = (event) => {
    captureAndRecognize(event.target.result);
  };
  reader.readAsDataURL(file);
}

// Open Actor Details Modal
function openActorModal(actor) {
  if (actorModalName) actorModalName.textContent = actor.name;
  if (actorModalRole) actorModalRole.textContent = `Роль: ${actor.character || 'Персонаж'}`;
  if (actorModalBio) actorModalBio.textContent = actor.bio || `${actor.name} исполняет роль ${actor.character || 'в фильме'}.`;

  const query = encodeURIComponent(actor.name);
  if (btnActorKp) {
    btnActorKp.onclick = () => {
      window.open(`https://www.kinopoisk.ru/index.php?kp_query=${query}`, '_blank');
    };
  }
  if (btnActorGoogle) {
    btnActorGoogle.onclick = () => {
      window.open(`https://www.google.com/search?q=${query}+актер`, '_blank');
    };
  }

  if (modalActor) modalActor.classList.add('open');
}

// Display Recognition Result
function displayResult(gemini, tmdb, capturedFrame) {
  try {
    const isTmdbFound = tmdb && tmdb.found;

    if (resultTitle) resultTitle.textContent = (isTmdbFound && tmdb.title) ? tmdb.title : (gemini.title || 'Фильм');
    if (resultOriginalTitle) resultOriginalTitle.textContent = (isTmdbFound && tmdb.originalTitle) ? tmdb.originalTitle : (gemini.originalTitle || '');
    
    // Type Badge
    if (resultType) resultType.textContent = (gemini.type || 'ФИЛЬМ').toUpperCase();

    // Director & Duration
    const director = gemini.director || 'Не указан';
    const duration = gemini.duration || (gemini.countries ? gemini.countries.join(', ') : 'Фильм');
    if (resultDirectorMeta) resultDirectorMeta.textContent = `Режиссер: ${director}`;
    if (resultDurationMeta) resultDurationMeta.textContent = `⏱ ${duration}`;

    // Year & Age
    if (resultYear) resultYear.textContent = (isTmdbFound && tmdb.releaseDate) ? tmdb.releaseDate.substring(0, 4) : (gemini.releaseYear ? gemini.releaseYear : '—');
    if (resultAge) resultAge.textContent = gemini.ageRating || '16+';

    // Ratings (IMDb & Kinopoisk)
    let imdbVal = (gemini.ratings && gemini.ratings.imdb) ? gemini.ratings.imdb : (isTmdbFound && tmdb.voteAverage ? tmdb.voteAverage.toFixed(1) : '—');
    let kpVal = (gemini.ratings && gemini.ratings.kinopoisk) ? gemini.ratings.kinopoisk : '—';

    if (resultRating) resultRating.textContent = `IMDb ${imdbVal}`;
    if (resultKpRating) resultKpRating.textContent = `КП ${kpVal}`;

    const confidenceMap = {
      high: 'Высокая точность',
      medium: 'Средняя точность',
      low: 'Примерное совпадение'
    };
    if (resultConfidence) resultConfidence.textContent = confidenceMap[gemini.confidence] || 'Распознано';

    // AI Explanation & Scene Breakdown
    let explanation = gemini.explanation || gemini.sceneDescription || 'Распознано по деталям кадра.';
    if (gemini.sceneDescription && gemini.explanation && gemini.sceneDescription !== gemini.explanation) {
      explanation = `${gemini.sceneDescription}\n\n🔍 ${gemini.explanation}`;
    }
    if (resultAiExplanation) resultAiExplanation.textContent = explanation;

    // Overview / Plot Summary
    if (resultOverview) resultOverview.textContent = gemini.overview || (isTmdbFound && tmdb.overview ? tmdb.overview : 'Сюжетное описание фильма формируется...');

    // Poster & Backdrop Handling
    if (isTmdbFound && tmdb.posterPath) {
      if (resultPoster) {
        resultPoster.src = tmdb.posterPath;
        resultPoster.style.display = 'block';
        resultPoster.onerror = () => {
          resultPoster.style.display = 'none';
          if (posterFallbackCard) posterFallbackCard.style.display = 'flex';
        };
      }
      if (posterFallbackCard) posterFallbackCard.style.display = 'none';
    } else {
      if (resultPoster) resultPoster.style.display = 'none';
      if (posterFallbackCard) {
        posterFallbackCard.style.display = 'flex';
        if (posterFallbackTitle) posterFallbackTitle.textContent = gemini.title || 'Кино';
      }
    }

    if (isTmdbFound && tmdb.backdropPath) {
      if (resultBackdrop) resultBackdrop.src = tmdb.backdropPath;
    } else if (capturedFrame) {
      if (resultBackdrop) resultBackdrop.src = capturedFrame;
    } else if (resultPoster && resultPoster.src) {
      if (resultBackdrop) resultBackdrop.src = resultPoster.src;
    }

    // Genres
    if (resultGenres) {
      resultGenres.innerHTML = '';
      const genres = (gemini.genres && gemini.genres.length > 0) ? gemini.genres : ((isTmdbFound && tmdb.genres) ? tmdb.genres : ['Кино', gemini.type || 'Фильм']);
      genres.forEach(g => {
        const span = document.createElement('span');
        span.className = 'genre-tag';
        span.textContent = g;
        resultGenres.appendChild(span);
      });
    }

    // Cast List (with click handlers)
    if (resultCast) {
      resultCast.innerHTML = '';
      const actorsList = (isTmdbFound && tmdb.cast && tmdb.cast.length > 0) ? tmdb.cast : (gemini.actors && gemini.actors.length > 0 ? gemini.actors : []);

      if (actorsList.length > 0) {
        if (castSection) castSection.style.display = 'block';
        actorsList.forEach(actor => {
          const item = document.createElement('div');
          item.className = 'cast-item';
          item.title = 'Нажмите для биографии';
          const initial = (actor.name && actor.name.length > 0) ? actor.name.charAt(0).toUpperCase() : '🎭';
          
          if (actor.profilePath) {
            item.innerHTML = `
              <img src="${actor.profilePath}" class="cast-avatar" onerror="this.outerHTML='<div class=\\'cast-avatar actor-avatar-icon\\'>${initial}</div>'">
              <span class="cast-name">${actor.name}</span>
              <span class="cast-role">${actor.character || 'В роли'}</span>
            `;
          } else {
            item.innerHTML = `
              <div class="cast-avatar actor-avatar-icon">${initial}</div>
              <span class="cast-name">${actor.name}</span>
              <span class="cast-role">${actor.character || 'В роли'}</span>
            `;
          }
          item.addEventListener('click', () => openActorModal(actor));
          resultCast.appendChild(item);
        });
      } else {
        if (castSection) castSection.style.display = 'none';
      }
    }

    // Interesting Facts
    if (resultFacts) {
      resultFacts.innerHTML = '';
      if (gemini.interestingFacts && gemini.interestingFacts.length > 0) {
        if (factsSection) factsSection.style.display = 'block';
        gemini.interestingFacts.forEach(fact => {
          const li = document.createElement('li');
          li.textContent = fact;
          resultFacts.appendChild(li);
        });
      } else {
        if (factsSection) factsSection.style.display = 'none';
      }
    }

    // Where to Watch
    if (resultWatchPlatforms) {
      resultWatchPlatforms.innerHTML = '';
      const platforms = (gemini.whereToWatch && gemini.whereToWatch.length > 0) ? gemini.whereToWatch : ['Кинопоиск', 'Иви', 'Okko', 'Premier'];
      platforms.forEach(p => {
        const chip = document.createElement('div');
        chip.className = 'platform-chip';
        chip.innerHTML = `<i data-lucide="play"></i> <span>${p}</span>`;
        chip.onclick = () => {
          window.open(`https://www.google.com/search?q=смотреть+${encodeURIComponent(gemini.title)}+онлайн+${encodeURIComponent(p)}`, '_blank');
        };
        resultWatchPlatforms.appendChild(chip);
      });
    }

    // Trailer Button
    if (btnWatchTrailer) {
      const trailerQuery = gemini.trailerQuery || `${gemini.title} русский трейлер`;
      btnWatchTrailer.onclick = () => {
        if (isTmdbFound && tmdb.trailer) {
          window.open(`https://www.youtube.com/watch?v=${tmdb.trailer}`, '_blank');
        } else {
          window.open(`https://www.youtube.com/results?search_query=${encodeURIComponent(trailerQuery)}`, '_blank');
        }
      };
    }

    // Open Sheet
    if (resultSheet) {
      resultSheet.classList.add('open');
      resultSheet.scrollTop = 0;
    }
    if (window.lucide) {
      try { lucide.createIcons(); } catch (e) { console.warn('Lucide icon error:', e); }
    }
  } catch (err) {
    console.error('Error rendering result sheet:', err);
    if (resultSheet) resultSheet.classList.add('open');
  }
}

function closeResultSheet() {
  resultSheet.classList.remove('open');
}

// Load Server Info & Generate QR for phone connect
async function loadServerInfo() {
  try {
    const res = await fetch('/api/server-info');
    if (!res.ok) return;
    const data = await res.json();
    
    if (data.connectionUrls && data.connectionUrls.length > 0) {
      const primaryUrl = data.connectionUrls[0];
      phoneConnectUrl.textContent = primaryUrl;

      // Render QR Code
      if (window.QRCode && qrcodeEl) {
        qrcodeEl.innerHTML = '';
        new QRCode(qrcodeEl, {
          text: primaryUrl,
          width: 160,
          height: 160,
          colorDark: '#090d16',
          colorLight: '#ffffff',
          correctLevel: QRCode.CorrectLevel.M
        });
      }
    }
  } catch (e) {
    console.log('Server info fetch failed:', e);
  }
}