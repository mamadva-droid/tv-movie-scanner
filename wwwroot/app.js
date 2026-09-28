// КиноГид AI — Voice & Text Movie Guide Application

let currentMovieData = null;
let speechRecognition = null;
let isListening = false;
let abortController = null;

// DOM Elements
const btnHome = document.getElementById('btn-home');
const heroSection = document.getElementById('hero-section');
const loadingSection = document.getElementById('loading-section');
const loadingTitle = document.getElementById('loading-title');
const loadingDesc = document.getElementById('loading-desc');
const movieDetailsSection = document.getElementById('movie-details-section');

// Search & Voice Controls
const movieSearchInput = document.getElementById('movie-search-input');
const btnClearInput = document.getElementById('btn-clear-input');
const btnSubmitSearch = document.getElementById('btn-submit-search');
const btnVoiceSearch = document.getElementById('btn-voice-search');
const voiceStatusText = document.getElementById('voice-status-text');
const btnBackToSearch = document.getElementById('btn-back-to-search');

// Movie Details Elements
const movieBackdrop = document.getElementById('movie-backdrop');
const moviePoster = document.getElementById('movie-poster');
const posterPlaceholder = document.getElementById('poster-placeholder');
const posterFallbackText = document.getElementById('poster-fallback-text');
const movieType = document.getElementById('movie-type');
const movieTitle = document.getElementById('movie-title');
const movieOrigTitle = document.getElementById('movie-orig-title');
const movieKpRating = document.getElementById('movie-kp-rating');
const movieImdbRating = document.getElementById('movie-imdb-rating');
const movieYear = document.getElementById('movie-year');
const movieDuration = document.getElementById('movie-duration');
const movieCountry = document.getElementById('movie-country');
const movieAge = document.getElementById('movie-age');
const movieDirector = document.getElementById('movie-director');
const movieGenres = document.getElementById('movie-genres');
const movieOverview = document.getElementById('movie-overview');
const movieCastGrid = document.getElementById('movie-cast-grid');
const movieFactsList = document.getElementById('movie-facts-list');
const btnOpenTrailer = document.getElementById('btn-open-trailer');
const btnToggleFavorite = document.getElementById('btn-toggle-favorite');
const favoriteIcon = document.getElementById('favorite-icon');
const favoriteBtnText = document.getElementById('favorite-btn-text');
const btnShareMovie = document.getElementById('btn-share-movie');

// Modals
const modalFavorites = document.getElementById('modal-favorites');
const btnOpenFavorites = document.getElementById('btn-open-favorites');
const btnCloseFavorites = document.getElementById('btn-close-favorites');
const favoritesList = document.getElementById('favorites-list');
const favoritesCounter = document.getElementById('favorites-counter');

const modalHistory = document.getElementById('modal-history');
const btnOpenHistory = document.getElementById('btn-open-history');
const btnCloseHistory = document.getElementById('btn-close-history');
const historyList = document.getElementById('history-list');
const btnClearHistory = document.getElementById('btn-clear-history');

const modalSettings = document.getElementById('modal-settings');
const btnOpenSettings = document.getElementById('btn-open-settings');
const btnCloseSettings = document.getElementById('btn-close-settings');
const inputKpKey = document.getElementById('input-kp-key');
const inputGeminiKey = document.getElementById('input-gemini-key');
const btnSaveSettings = document.getElementById('btn-save-settings');

const modalActor = document.getElementById('modal-actor');
const btnCloseActor = document.getElementById('btn-close-actor');
const actorModalName = document.getElementById('actor-modal-name');
const actorModalRole = document.getElementById('actor-modal-role');
const actorModalBio = document.getElementById('actor-modal-bio');
const actorModalPhoto = document.getElementById('actor-modal-photo');
const btnActorKp = document.getElementById('btn-actor-kp');
const btnActorGoogle = document.getElementById('btn-actor-google');

const toastPopup = document.getElementById('toast-popup');

// Initialize Application
document.addEventListener('DOMContentLoaded', () => {
  initIcons();
  initEventListeners();
  initVoiceRecognition();
  loadSavedSettings();
  updateFavoritesCounter();

  // Register PWA Service Worker
  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('/sw.js').catch(err => console.log('SW error:', err));
  }
});

function initIcons() {
  if (window.lucide) {
    try { lucide.createIcons(); } catch (e) { }
  }
}

// Toast Popup
function showToast(message, duration = 3000) {
  if (!toastPopup) return;
  toastPopup.textContent = message;
  toastPopup.classList.add('show');
  setTimeout(() => {
    toastPopup.classList.remove('show');
  }, duration);
}

// Voice Recognition Initialization
function initVoiceRecognition() {
  const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!SpeechRecognition) {
    if (voiceStatusText) voiceStatusText.textContent = 'Голосовой ввод не поддерживается браузером';
    return;
  }

  speechRecognition = new SpeechRecognition();
  speechRecognition.lang = 'ru-RU';
  speechRecognition.continuous = false;
  speechRecognition.interimResults = true;

  speechRecognition.onstart = () => {
    isListening = true;
    if (btnVoiceSearch) btnVoiceSearch.classList.add('listening');
    if (voiceStatusText) voiceStatusText.textContent = '🎙️ Слушаю вас... Говорите название фильма';
  };

  speechRecognition.onresult = (event) => {
    const transcript = Array.from(event.results)
      .map(result => result[0])
      .map(result => result.transcript)
      .join('');

    if (movieSearchInput) {
      movieSearchInput.value = transcript;
      if (btnClearInput) btnClearInput.style.display = transcript.length > 0 ? 'flex' : 'none';
    }

    if (event.results[0].isFinal) {
      stopVoiceListening();
      if (transcript.trim()) {
        executeSmartSearch(transcript.trim());
      }
    }
  };

  speechRecognition.onerror = (event) => {
    stopVoiceListening();
    if (event.error === 'not-allowed') {
      showToast('Разрешите доступ к микрофону в настройках браузера');
    } else if (event.error !== 'no-speech') {
      showToast('Ошибка микрофона: ' + event.error);
    }
  };

  speechRecognition.onend = () => {
    stopVoiceListening();
  };
}

function startVoiceListening() {
  if (!speechRecognition) {
    showToast('Голосовой поиск поддерживается в Google Chrome, Safari, Edge');
    return;
  }
  try {
    if (voiceStatusText) voiceStatusText.style.display = 'flex';
    speechRecognition.start();
  } catch (e) {
    speechRecognition.stop();
  }
}

function stopVoiceListening() {
  isListening = false;
  if (btnVoiceSearch) btnVoiceSearch.classList.remove('listening');
  if (voiceStatusText) voiceStatusText.style.display = 'none';
}

// Event Listeners
function initEventListeners() {
  // Home Click
  if (btnHome) {
    btnHome.addEventListener('click', () => {
      showSearchView();
    });
  }

  if (btnBackToSearch) {
    btnBackToSearch.addEventListener('click', () => {
      showSearchView();
    });
  }

  // Voice Search Trigger
  if (btnVoiceSearch) {
    btnVoiceSearch.addEventListener('click', () => {
      if (isListening) {
        if (speechRecognition) speechRecognition.stop();
      } else {
        startVoiceListening();
      }
    });
  }

  // Search Input Handlers
  if (movieSearchInput) {
    movieSearchInput.addEventListener('input', () => {
      if (btnClearInput) {
        btnClearInput.style.display = movieSearchInput.value.trim().length > 0 ? 'flex' : 'none';
      }
    });

    movieSearchInput.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        movieSearchInput.blur();
        const q = movieSearchInput.value.trim();
        if (q) executeSmartSearch(q);
      }
    });
  }

  if (btnClearInput) {
    btnClearInput.addEventListener('click', () => {
      if (movieSearchInput) {
        movieSearchInput.value = '';
        btnClearInput.style.display = 'none';
        movieSearchInput.focus();
      }
    });
  }

  if (btnSubmitSearch) {
    btnSubmitSearch.addEventListener('click', () => {
      const q = movieSearchInput ? movieSearchInput.value.trim() : '';
      if (q) executeSmartSearch(q);
      else showToast('Введите название фильма или имя актера');
    });
  }

  // Suggestion Chips
  document.querySelectorAll('.chip-item').forEach(chip => {
    chip.addEventListener('click', () => {
      const q = chip.getAttribute('data-query');
      if (q) {
        if (movieSearchInput) {
          movieSearchInput.value = q;
          if (btnClearInput) btnClearInput.style.display = 'flex';
        }
        executeSmartSearch(q);
      }
    });
  });

  // Action Buttons
  if (btnOpenTrailer) {
    btnOpenTrailer.addEventListener('click', () => {
      if (!currentMovieData) return;
      const q = encodeURIComponent(currentMovieData.trailerQuery || `${currentMovieData.title} трейлер`);
      window.open(`https://www.youtube.com/results?search_query=${q}`, '_blank');
    });
  }

  if (btnToggleFavorite) {
    btnToggleFavorite.addEventListener('click', toggleFavoriteCurrent);
  }

  if (btnShareMovie) {
    btnShareMovie.addEventListener('click', () => {
      if (!currentMovieData) return;
      if (navigator.share) {
        navigator.share({
          title: currentMovieData.title,
          text: `Смотри фильм «${currentMovieData.title}» (${currentMovieData.releaseYear}): ${currentMovieData.overview}`,
          url: window.location.href
        }).catch(() => {});
      } else {
        navigator.clipboard.writeText(`${currentMovieData.title} (${currentMovieData.releaseYear})`);
        showToast('Название фильма скопировано!');
      }
    });
  }

  // Modals Open/Close
  if (btnOpenFavorites && modalFavorites) {
    btnOpenFavorites.addEventListener('click', () => {
      renderFavoritesModal();
      modalFavorites.classList.add('open');
    });
  }
  if (btnCloseFavorites && modalFavorites) {
    btnCloseFavorites.addEventListener('click', () => modalFavorites.classList.remove('open'));
  }

  if (btnOpenHistory && modalHistory) {
    btnOpenHistory.addEventListener('click', () => {
      renderHistoryModal();
      modalHistory.classList.add('open');
    });
  }
  if (btnCloseHistory && modalHistory) {
    btnCloseHistory.addEventListener('click', () => modalHistory.classList.remove('open'));
  }
  if (btnClearHistory) {
    btnClearHistory.addEventListener('click', () => {
      localStorage.removeItem('movie_search_history');
      renderHistoryModal();
      showToast('История очищена');
    });
  }

  if (btnOpenSettings && modalSettings) {
    btnOpenSettings.addEventListener('click', () => modalSettings.classList.add('open'));
  }
  if (btnCloseSettings && modalSettings) {
    btnCloseSettings.addEventListener('click', () => modalSettings.classList.remove('open'));
  }
  if (btnSaveSettings) {
    btnSaveSettings.addEventListener('click', saveSettings);
  }

  if (btnCloseActor && modalActor) {
    btnCloseActor.addEventListener('click', () => modalActor.classList.remove('open'));
  }

  // Backdrop click to close modals
  [modalFavorites, modalHistory, modalSettings, modalActor].forEach(modal => {
    if (modal) {
      modal.addEventListener('click', (e) => {
        if (e.target === modal) modal.classList.remove('open');
      });
    }
  });

  // Escape key
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
      [modalFavorites, modalHistory, modalSettings, modalActor].forEach(m => {
        if (m) m.classList.remove('open');
      });
    }
  });
}

// Show / Hide Views
function showSearchView() {
  if (heroSection) heroSection.style.display = 'flex';
  if (loadingSection) loadingSection.style.display = 'none';
  if (movieDetailsSection) movieDetailsSection.style.display = 'none';
  window.scrollTo({ top: 0, behavior: 'smooth' });
}

function showLoadingView(title, desc) {
  if (heroSection) heroSection.style.display = 'none';
  if (loadingSection) loadingSection.style.display = 'flex';
  if (movieDetailsSection) movieDetailsSection.style.display = 'none';
  if (loadingTitle) loadingTitle.textContent = title || 'Ищем фильм...';
  if (loadingDesc) loadingDesc.textContent = desc || 'Загрузка официальных постеров, рейтингов и актеров';
  initIcons();
}

function showDetailsView() {
  if (heroSection) heroSection.style.display = 'none';
  if (loadingSection) loadingSection.style.display = 'none';
  if (movieDetailsSection) movieDetailsSection.style.display = 'flex';
  window.scrollTo({ top: 0, behavior: 'smooth' });
  initIcons();
}

// Main Smart Search Function
async function executeSmartSearch(query) {
  if (!query) return;

  if (abortController) abortController.abort();
  abortController = new AbortController();

  showLoadingView(`Ищем: «${query}»`, 'Поиск по базе Кинопоиска, загрузка постеров и актерского состава...');

  try {
    const geminiKey = localStorage.getItem('gemini_api_key') || '';
    const searchUrl = `/api/smart-search?query=${encodeURIComponent(query)}&clientApiKey=${encodeURIComponent(geminiKey)}`;

    const response = await fetch(searchUrl, { signal: abortController.signal });
    const data = await response.json();

    if (!response.ok) {
      throw new Error(data.error || 'Фильм не найден. Попробуйте уточнить название.');
    }

    currentMovieData = data;
    saveToHistory(query, data);
    renderMovieDetails(data);
    showDetailsView();

  } catch (err) {
    if (err.name === 'AbortError') return;
    console.error('Search error:', err);
    showToast(err.message || 'Ошибка поиска. Попробуйте еще раз.', 4000);
    showSearchView();
  }
}

// Render Movie Dossier
function renderMovieDetails(movie) {
  if (!movie) return;

  // Titles
  if (movieTitle) movieTitle.textContent = movie.title || 'Фильм';
  if (movieOrigTitle) movieOrigTitle.textContent = movie.originalTitle || '';
  if (movieType) movieType.textContent = (movie.type || 'ФИЛЬМ').toUpperCase();

  // Ratings
  const kp = (movie.ratings && movie.ratings.kinopoisk) ? Number(movie.ratings.kinopoisk).toFixed(1) : '—';
  const imdb = (movie.ratings && movie.ratings.imdb) ? Number(movie.ratings.imdb).toFixed(1) : '—';
  if (movieKpRating) movieKpRating.textContent = kp;
  if (movieImdbRating) movieImdbRating.textContent = imdb;

  // Meta
  if (movieYear) movieYear.innerHTML = `<i data-lucide="calendar"></i> ${movie.releaseYear || '—'}`;
  if (movieDuration) movieDuration.innerHTML = `<i data-lucide="clock"></i> ${movie.duration || '—'}`;
  const countries = movie.countries && movie.countries.length > 0 ? movie.countries.join(', ') : 'Мир';
  if (movieCountry) movieCountry.innerHTML = `<i data-lucide="globe"></i> ${countries}`;
  if (movieAge) movieAge.textContent = movie.ageRating || '16+';
  if (movieDirector) movieDirector.textContent = movie.director || 'Не указан';

  // Genres
  if (movieGenres) {
    movieGenres.innerHTML = '';
    const genres = movie.genres && movie.genres.length > 0 ? movie.genres : ['Кино'];
    genres.forEach(g => {
      const chip = document.createElement('span');
      chip.className = 'genre-chip';
      chip.textContent = g;
      movieGenres.appendChild(chip);
    });
  }

  // Posters & Backdrops
  if (movie.posterPath) {
    if (moviePoster) {
      moviePoster.src = movie.posterPath;
      moviePoster.style.display = 'block';
      moviePoster.onerror = () => {
        moviePoster.style.display = 'none';
        if (posterPlaceholder) posterPlaceholder.style.display = 'flex';
      };
    }
    if (posterPlaceholder) posterPlaceholder.style.display = 'none';
  } else {
    if (moviePoster) moviePoster.style.display = 'none';
    if (posterPlaceholder) {
      posterPlaceholder.style.display = 'flex';
      if (posterFallbackText) posterFallbackText.textContent = movie.title || 'Кино';
    }
  }

  if (movieBackdrop) {
    movieBackdrop.src = movie.backdropPath || movie.posterPath || '';
  }

  // Overview / Synopsis
  if (movieOverview) {
    movieOverview.textContent = movie.overview || 'Сюжетное описание формируется...';
  }

  // Cast List
  if (movieCastGrid) {
    movieCastGrid.innerHTML = '';
    const actors = movie.actors || [];
    if (actors.length > 0) {
      document.getElementById('actors-panel').style.display = 'flex';
      actors.forEach(actor => {
        const card = document.createElement('div');
        card.className = 'actor-card';
        const initial = actor.name ? actor.name.charAt(0).toUpperCase() : '🎭';
        
        const photoHtml = actor.profilePath
          ? `<img src="${actor.profilePath}" alt="${actor.name}" class="actor-img" onerror="this.outerHTML='<div class=\\'actor-placeholder\\'>${initial}</div>'">`
          : `<div class="actor-placeholder">${initial}</div>`;

        card.innerHTML = `
          <div class="actor-photo-wrapper">${photoHtml}</div>
          <div class="actor-info">
            <span class="actor-name" title="${actor.name}">${actor.name}</span>
            <span class="actor-character" title="${actor.character || 'Роль'}">${actor.character || 'В роли'}</span>
          </div>
        `;
        card.addEventListener('click', () => openActorModal(actor));
        movieCastGrid.appendChild(card);
      });
    } else {
      document.getElementById('actors-panel').style.display = 'none';
    }
  }

  // Facts List
  if (movieFactsList) {
    movieFactsList.innerHTML = '';
    const facts = movie.interestingFacts || [];
    if (facts.length > 0) {
      document.getElementById('facts-panel').style.display = 'flex';
      facts.forEach(f => {
        const li = document.createElement('li');
        li.textContent = f;
        movieFactsList.appendChild(li);
      });
    } else {
      document.getElementById('facts-panel').style.display = 'none';
    }
  }

  updateFavoriteButtonState();
  initIcons();
}

// Actor Details Modal
function openActorModal(actor) {
  if (!actor) return;
  if (actorModalName) actorModalName.textContent = actor.name;
  if (actorModalRole) actorModalRole.textContent = `В роли: ${actor.character || 'Персонаж'}`;
  if (actorModalBio) actorModalBio.textContent = actor.bio || `${actor.name} исполняет роль в фильме «${currentMovieData ? currentMovieData.title : ''}».`;

  if (actorModalPhoto) {
    if (actor.profilePath) {
      actorModalPhoto.src = actor.profilePath;
      actorModalPhoto.style.display = 'block';
    } else {
      actorModalPhoto.style.display = 'none';
    }
  }

  const query = encodeURIComponent(actor.name);
  if (btnActorKp) {
    btnActorKp.onclick = () => window.open(`https://www.kinopoisk.ru/index.php?kp_query=${query}`, '_blank');
  }
  if (btnActorGoogle) {
    btnActorGoogle.onclick = () => window.open(`https://www.google.com/search?q=${query}+актер`, '_blank');
  }

  if (modalActor) modalActor.classList.add('open');
  initIcons();
}

// Favorites Management
function getFavorites() {
  try {
    return JSON.parse(localStorage.getItem('movie_favorites') || '[]');
  } catch {
    return [];
  }
}

function isCurrentMovieFavorite() {
  if (!currentMovieData) return false;
  const favs = getFavorites();
  return favs.some(f => f.title === currentMovieData.title && f.releaseYear === currentMovieData.releaseYear);
}

function updateFavoriteButtonState() {
  const isFav = isCurrentMovieFavorite();
  if (btnToggleFavorite) {
    if (isFav) {
      btnToggleFavorite.classList.add('saved');
      if (favoriteBtnText) favoriteBtnText.textContent = 'В избранном';
      if (favoriteIcon) favoriteIcon.setAttribute('data-lucide', 'bookmark-check');
    } else {
      btnToggleFavorite.classList.remove('saved');
      if (favoriteBtnText) favoriteBtnText.textContent = 'В избранное';
      if (favoriteIcon) favoriteIcon.setAttribute('data-lucide', 'bookmark');
    }
    initIcons();
  }
}

function toggleFavoriteCurrent() {
  if (!currentMovieData) return;
  let favs = getFavorites();
  const exists = favs.findIndex(f => f.title === currentMovieData.title && f.releaseYear === currentMovieData.releaseYear);

  if (exists >= 0) {
    favs.splice(exists, 1);
    showToast('Удалено из избранного');
  } else {
    favs.unshift({
      title: currentMovieData.title,
      originalTitle: currentMovieData.originalTitle,
      releaseYear: currentMovieData.releaseYear,
      posterPath: currentMovieData.posterPath,
      ratings: currentMovieData.ratings,
      savedAt: new Date().toISOString()
    });
    showToast('Сохранено в избранное! ⭐');
  }

  localStorage.setItem('movie_favorites', JSON.stringify(favs));
  updateFavoriteButtonState();
  updateFavoritesCounter();
}

function updateFavoritesCounter() {
  const favs = getFavorites();
  if (favoritesCounter) {
    favoritesCounter.textContent = favs.length;
    favoritesCounter.style.display = favs.length > 0 ? 'block' : 'none';
  }
}

function renderFavoritesModal() {
  if (!favoritesList) return;
  const favs = getFavorites();
  if (favs.length === 0) {
    favoritesList.innerHTML = `
      <div class="empty-state">
        <i data-lucide="bookmark-x"></i>
        <p>В избранном пока ничего нет.<br>Нажмите «В избранное» на карточке любого фильма.</p>
      </div>
    `;
  } else {
    favoritesList.innerHTML = '';
    favs.forEach(f => {
      const row = document.createElement('div');
      row.className = 'saved-item-row';
      row.innerHTML = `
        <div>
          <div class="saved-item-title">${f.title}</div>
          <div class="saved-item-year">${f.releaseYear || ''} • КП ${f.ratings?.kinopoisk || '—'}</div>
        </div>
        <i data-lucide="chevron-right"></i>
      `;
      row.addEventListener('click', () => {
        if (modalFavorites) modalFavorites.classList.remove('open');
        executeSmartSearch(f.title);
      });
      favoritesList.appendChild(row);
    });
  }
  initIcons();
}

// Search History Management
function saveToHistory(query, movie) {
  try {
    let history = JSON.parse(localStorage.getItem('movie_search_history') || '[]');
    history = history.filter(h => h.query.toLowerCase() !== query.toLowerCase());
    history.unshift({
      query,
      title: movie ? movie.title : query,
      year: movie ? movie.releaseYear : '',
      timestamp: new Date().toISOString()
    });
    if (history.length > 25) history.pop();
    localStorage.setItem('movie_search_history', JSON.stringify(history));
  } catch { }
}

function renderHistoryModal() {
  if (!historyList) return;
  let history = [];
  try { history = JSON.parse(localStorage.getItem('movie_search_history') || '[]'); } catch { }

  if (history.length === 0) {
    historyList.innerHTML = `
      <div class="empty-state">
        <i data-lucide="history"></i>
        <p>История поисков пуста.</p>
      </div>
    `;
  } else {
    historyList.innerHTML = '';
    history.forEach(h => {
      const row = document.createElement('div');
      row.className = 'saved-item-row';
      row.innerHTML = `
        <div>
          <div class="saved-item-title">${h.query}</div>
          <div class="saved-item-year">${h.title !== h.query ? `Найдено: ${h.title}` : 'Поисковый запрос'}</div>
        </div>
        <i data-lucide="arrow-up-right"></i>
      `;
      row.addEventListener('click', () => {
        if (modalHistory) modalHistory.classList.remove('open');
        if (movieSearchInput) movieSearchInput.value = h.query;
        executeSmartSearch(h.query);
      });
      historyList.appendChild(row);
    });
  }
  initIcons();
}

// Settings Sync
async function loadSavedSettings() {
  const kp = localStorage.getItem('kinopoisk_api_key') || '8c8e1a50-6322-4135-8875-5d40a5420d86';
  const gemini = localStorage.getItem('gemini_api_key') || '';
  if (inputKpKey) inputKpKey.value = kp;
  if (inputGeminiKey) inputGeminiKey.value = gemini;
}

async function saveSettings() {
  const kp = inputKpKey ? inputKpKey.value.trim() : '';
  const gemini = inputGeminiKey ? inputGeminiKey.value.trim() : '';

  localStorage.setItem('kinopoisk_api_key', kp || '8c8e1a50-6322-4135-8875-5d40a5420d86');
  localStorage.setItem('gemini_api_key', gemini);

  try {
    await fetch('/api/save-keys', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        kinopoiskApiKey: kp,
        geminiApiKey: gemini
      })
    });
  } catch (e) { }

  if (modalSettings) modalSettings.classList.remove('open');
  showToast('Настройки успешно сохранены!');
}
