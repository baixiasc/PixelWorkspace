// ============================================================
//  局域网文件查看 - 前端逻辑
// ============================================================

const TOKEN_KEY = 'lan_file_token';

const IMAGE_EXTENSIONS = ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg', 'ico', 'avif'];
const VIDEO_EXTENSIONS = ['mp4', 'webm', 'mkv', 'avi', 'mov', 'm4v', 'wmv', 'flv', '3gp', 'ts'];

const FILE_ICONS = {
  dir: '<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3.5 7.5a2 2 0 0 1 2-2h4l2 2h7a2 2 0 0 1 2 2v7.5a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2Z"></path></svg>',
  image: '<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3.5" y="4" width="17" height="16" rx="3"></rect><circle cx="9" cy="9" r="1.5"></circle><path d="m5.5 17 4.2-4.2 3 3 2.1-2.1 3.7 3.3"></path></svg>',
  video: '<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3.5" y="5" width="17" height="14" rx="3"></rect><path d="m10 9 5 3-5 3Z"></path></svg>',
  other: '<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M6 3.5h7l5 5V19a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V5.5a2 2 0 0 1 2-2Z"></path><path d="M13 3.5V9h5"></path><path d="M8 13h8M8 17h5"></path></svg>',
};

const CHEVRON_ICON = '<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m9 18 6-6-6-6"></path></svg>';

let currentToken = localStorage.getItem(TOKEN_KEY) || null;
let currentPath = '';
let currentData = null;
let currentImages = [];
let currentImageIndex = -1;
let imageTouchStartX = 0;
let imageTouchStartY = 0;
let imageSwipeDetected = false;
let sortMode = localStorage.getItem('lan_file_sort') || 'default';

// ============================================================
//  DOM 引用
// ============================================================

const loginScreen = document.getElementById('login-screen');
const mainScreen = document.getElementById('main-screen');
const passwordInput = document.getElementById('password-input');
const loginButton = document.getElementById('login-button');
const loginError = document.getElementById('login-error');
const backBtn = document.getElementById('back-btn');
const refreshBtn = document.getElementById('refresh-btn');
const logoutBtn = document.getElementById('logout-btn');
const shutdownBtn = document.getElementById('shutdown-btn');
const sortSelect = document.getElementById('sort-select');
const breadcrumb = document.getElementById('breadcrumb');
const fileList = document.getElementById('file-list');
const emptyState = document.getElementById('empty-state');
const loadingOverlay = document.getElementById('loading');
const toastEl = document.getElementById('toast');
const imageViewer = document.getElementById('image-viewer');
const imageCounter = document.getElementById('image-counter');
const imageFullscreenBtn = document.getElementById('image-fullscreen-btn');
const fullscreenEnterIcon = imageFullscreenBtn.querySelector('.fullscreen-enter-icon');
const fullscreenExitIcon = imageFullscreenBtn.querySelector('.fullscreen-exit-icon');
const viewerImage = document.getElementById('viewer-image');
const imagePrevBtn = document.getElementById('image-prev');
const imageNextBtn = document.getElementById('image-next');
const videoPlayer = document.getElementById('video-player');
const playerVideo = document.getElementById('player-video');

sortSelect.value = sortMode;
sortSelect.addEventListener('change', () => {
  sortMode = sortSelect.value;
  localStorage.setItem('lan_file_sort', sortMode);
  if (currentData) renderFiles(currentData);
  showToast('排序已更新');
});

// ============================================================
//  工具函数
// ============================================================

function getFileExt(name) {
  const dot = name.lastIndexOf('.');
  if (dot === -1) return '';
  return name.substring(dot + 1).toLowerCase();
}

function isImage(name) {
  return IMAGE_EXTENSIONS.includes(getFileExt(name));
}

function isVideo(name) {
  return VIDEO_EXTENSIONS.includes(getFileExt(name));
}

function formatSize(bytes) {
  if (!bytes) return '';
  if (bytes < 1024) return bytes + ' B';
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
  if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  return (bytes / (1024 * 1024 * 1024)).toFixed(2) + ' GB';
}

function showToast(message, duration = 2000) {
  toastEl.textContent = message;
  toastEl.classList.remove('hidden');
  clearTimeout(showToast._timer);
  showToast._timer = setTimeout(() => {
    toastEl.classList.add('hidden');
  }, duration);
}

function showLoading() {
  loadingOverlay.classList.remove('hidden');
}

function hideLoading() {
  loadingOverlay.classList.add('hidden');
}

// 触发浏览器下载文件
function downloadItem(item) {
  const a = document.createElement('a');
  a.href = fileUrl(item);
  a.download = item.name;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
}

// ============================================================
//  认证相关
// ============================================================

async function apiFetch(url, options = {}) {
  const headers = { ...(options.headers || {}) };
  if (currentToken) {
    headers['Authorization'] = 'Bearer ' + currentToken;
  }

  const response = await fetch(url, { ...options, headers });

  // 401 = 需要重新登录
  if (response.status === 401) {
    currentToken = null;
    localStorage.removeItem(TOKEN_KEY);
    showLoginScreen();
    const data = await response.json().catch(() => ({}));
    throw new Error(data.error || '登录已过期');
  }

  if (!response.ok) {
    const data = await response.json().catch(() => ({}));
    throw new Error(data.error || '请求失败');
  }

  return response.json();
}

async function handleLogin() {
  const password = passwordInput.value.trim();
  if (!password) {
    showLoginError('请输入密码');
    return;
  }

  loginButton.disabled = true;
  loginButton.textContent = '登录中...';
  hideLoginError();

  try {
    const response = await fetch('/api/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ password }),
    });

    const data = await response.json().catch(() => ({}));

    if (!response.ok) {
      showLoginError(data.error || '登录失败');
      return;
    }

    currentToken = data.token;
    localStorage.setItem(TOKEN_KEY, currentToken);

    passwordInput.value = '';
    enterApp();
  } catch (e) {
    showLoginError('网络错误，请检查连接');
  } finally {
    loginButton.disabled = false;
    loginButton.textContent = '授权此设备';
  }
}

function showLoginError(message) {
  loginError.textContent = message;
  loginError.classList.remove('hidden');
}

function hideLoginError() {
  loginError.classList.add('hidden');
}

function showLoginScreen() {
  loginScreen.classList.remove('hidden');
  mainScreen.classList.add('hidden');
  setTimeout(() => passwordInput.focus(), 100);
}

function enterApp() {
  loginScreen.classList.add('hidden');
  mainScreen.classList.remove('hidden');
  currentPath = '';
  loadPath('');
}

// 启动时检查已保存的 token 是否有效
async function init() {
  if (currentToken) {
    try {
      await apiFetch('/api/verify');
      // 验证通过，直接进入
      loginScreen.classList.add('hidden');
      mainScreen.classList.remove('hidden');
      loadPath('');
      return;
    } catch (e) {
      // token 无效，显示登录界面
      currentToken = null;
      localStorage.removeItem(TOKEN_KEY);
    }
  }
  showLoginScreen();
}

// ============================================================
//  文件浏览
// ============================================================

function fileUrl(item) {
  return '/api/file?path=' + encodeURIComponent(item.path);
}

function thumbnailUrl(item) {
  return '/api/thumbnail?path=' + encodeURIComponent(item.path);
}

async function loadPath(path) {
  currentPath = path;
  backBtn.disabled = false;
  backBtn.setAttribute('aria-disabled', 'false');
  showLoading();
  fileList.innerHTML = '';
  emptyState.classList.add('hidden');

  try {
    const data = await apiFetch('/api/browse?path=' + encodeURIComponent(path));
    currentData = data;
    renderBreadcrumb(data);
    renderFiles(data);
  } catch (e) {
    emptyState.classList.remove('hidden');
    emptyState.querySelector('p').textContent = '加载失败: ' + e.message;
  } finally {
    hideLoading();
  }
}

function renderBreadcrumb(data) {
  if (data.isRoot) {
    breadcrumb.textContent = '我的文件';
  } else {
    breadcrumb.textContent = getBaseName(data.path) || data.path;
  }

  backBtn.disabled = Boolean(data.isRoot);
  backBtn.setAttribute('aria-disabled', String(Boolean(data.isRoot)));
}

// 获取路径最后一段（兼容 Windows 反斜杠路径）
function getBaseName(p) {
  const parts = p.split(/[\\/]/);
  return parts[parts.length - 1] || p;
}

function compareItems(a, b) {
  const nameCompare = a.name.localeCompare(b.name, 'zh-CN', { numeric: true, sensitivity: 'base' });
  if (sortMode === 'name-desc') return -nameCompare;
  if (sortMode === 'modified-desc') return (new Date(b.modified || 0) - new Date(a.modified || 0)) || nameCompare;
  if (sortMode === 'modified-asc') return (new Date(a.modified || 0) - new Date(b.modified || 0)) || nameCompare;
  if (sortMode === 'size-desc') return (b.size || 0) - (a.size || 0) || nameCompare;
  if (sortMode === 'size-asc') return (a.size || 0) - (b.size || 0) || nameCompare;
  if (a.isDirectory !== b.isDirectory) return a.isDirectory ? -1 : 1;
  return nameCompare;
}

function renderFiles(data) {
  fileList.innerHTML = '';
  const items = [...(data.items || [])].sort(compareItems);
  currentImages = items.filter((item) => !item.isDirectory && !item.isRoot && isImage(item.name));

  // 空目录
  if (items.length === 0) {
    emptyState.classList.remove('hidden');
    emptyState.querySelector('p').textContent = '此文件夹为空';
    return;
  }

  items.forEach((item, index) => {
    const div = document.createElement('button');
    div.type = 'button';
    div.className = 'file-item';
    div.style.animationDelay = Math.min(index * 0.025, 0.2) + 's';

    const isDir = item.isDirectory || item.isRoot;
    const img = isImage(item.name);
    const vid = isVideo(item.name);
    const type = isDir ? 'dir' : img ? 'image' : vid ? 'video' : 'other';
    const action = isDir ? '打开文件夹' : img ? '预览图片' : vid ? '播放视频' : '下载文件';
    div.setAttribute('aria-label', `${action}：${item.name}`);

    // 图片显示缩略图，其他类型保留原图标
    const icon = document.createElement('span');
    icon.className = 'file-icon ' + type;
    if (img && !isDir) {
      const thumbnail = document.createElement('img');
      thumbnail.className = 'file-thumbnail';
      thumbnail.src = thumbnailUrl(item);
      thumbnail.alt = '';
      thumbnail.loading = 'lazy';
      thumbnail.decoding = 'async';
      thumbnail.onerror = () => {
        icon.classList.add('thumbnail-fallback');
        icon.innerHTML = FILE_ICONS.image;
      };
      icon.appendChild(thumbnail);
    } else {
      icon.innerHTML = FILE_ICONS[type];
    }

    // 信息
    const info = document.createElement('span');
    info.className = 'file-info';

    const name = document.createElement('span');
    name.className = 'file-name';
    name.textContent = item.name;

    const meta = document.createElement('span');
    meta.className = 'file-meta';
    if (isDir) {
      const imageCount = Number.isFinite(item.imageCount) ? item.imageCount : 0;
      meta.textContent = `文件夹 · ${imageCount} 张图片`;
    } else {
      const parts = [];
      if (item.size) parts.push(formatSize(item.size));
      if (item.modified) {
        const d = new Date(item.modified);
        parts.push(d.toLocaleDateString('zh-CN') + ' ' + d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' }));
      }
      meta.textContent = parts.join(' · ');
    }

    info.appendChild(name);
    info.appendChild(meta);

    // 文件夹层级提示
    const arrow = document.createElement('span');
    arrow.className = 'file-arrow';
    if (isDir) arrow.innerHTML = CHEVRON_ICON;

    div.appendChild(icon);
    div.appendChild(info);
    div.appendChild(arrow);

    div.addEventListener('click', () => {
      if (isDir) {
        loadPath(item.path);
      } else if (img) {
        openImage(item);
      } else if (vid) {
        openVideo(item);
      } else {
        showToast('该类型文件无法预览，正在下载...');
        window.location.href = fileUrl(item);
      }
    });

    fileList.appendChild(div);
  });
}

// ============================================================
//  图片/视频预览
// ============================================================

function openImage(item) {
  currentImageIndex = currentImages.findIndex((image) => image.path === item.path);
  if (currentImageIndex < 0) currentImageIndex = 0;
  showCurrentImage();
}

function showCurrentImage() {
  const item = currentImages[currentImageIndex];
  if (!item) return;

  // 加载失败时自动下载兜底
  viewerImage.onerror = () => {
    closeViewer();
    showToast('该图片无法预览，正在下载...');
    setTimeout(() => downloadItem(item), 600);
  };
  viewerImage.src = fileUrl(item);
  viewerImage.alt = item.name;
  imageCounter.textContent = `${currentImageIndex + 1}/${currentImages.length}`;
  imagePrevBtn.disabled = currentImageIndex <= 0;
  imageNextBtn.disabled = currentImageIndex >= currentImages.length - 1;
  imageViewer.classList.remove('hidden');
}

function showPreviousImage() {
  if (currentImageIndex > 0) {
    currentImageIndex -= 1;
    showCurrentImage();
  }
}

function showNextImage() {
  if (currentImageIndex < currentImages.length - 1) {
    currentImageIndex += 1;
    showCurrentImage();
  }
}

function openVideo(item) {
  playerVideo.onerror = () => {
    closeViewer();
    showToast('该视频格式无法直接播放，正在下载...');
    setTimeout(() => downloadItem(item), 600);
  };
  playerVideo.src = fileUrl(item);
  videoPlayer.classList.remove('hidden');
  playerVideo.play().catch(() => {});
}

function isFullscreen() {
  return Boolean(document.fullscreenElement || document.webkitFullscreenElement);
}

function updateFullscreenButton() {
  const fullscreen = isFullscreen();
  fullscreenEnterIcon.classList.toggle('hidden', fullscreen);
  fullscreenExitIcon.classList.toggle('hidden', !fullscreen);
  imageFullscreenBtn.setAttribute('aria-label', fullscreen ? '退出全屏' : '进入全屏');
  imageFullscreenBtn.title = fullscreen ? '退出全屏' : '进入全屏';
}

async function toggleImageFullscreen() {
  try {
    if (isFullscreen()) {
      if (document.exitFullscreen) {
        await document.exitFullscreen();
      } else if (document.webkitExitFullscreen) {
        document.webkitExitFullscreen();
      }
    } else if (imageViewer.requestFullscreen) {
      await imageViewer.requestFullscreen();
    } else if (imageViewer.webkitRequestFullscreen) {
      imageViewer.webkitRequestFullscreen();
    } else {
      showToast('当前浏览器不支持全屏');
    }
  } catch (e) {
    showToast('无法切换全屏');
  }
  updateFullscreenButton();
}

function closeViewer() {
  if (isFullscreen()) {
    if (document.exitFullscreen) {
      document.exitFullscreen().catch(() => {});
    } else if (document.webkitExitFullscreen) {
      document.webkitExitFullscreen();
    }
  }
  updateFullscreenButton();
  imageViewer.classList.add('hidden');
  videoPlayer.classList.add('hidden');
  viewerImage.onerror = null;
  playerVideo.onerror = null;
  viewerImage.src = '';
  viewerImage.alt = '图片预览';
  imagePrevBtn.disabled = true;
  imageNextBtn.disabled = true;
  imageCounter.textContent = '0/0';
  currentImageIndex = -1;
  playerVideo.pause();
  playerVideo.src = '';
}

// ============================================================
//  事件绑定
// ============================================================

loginButton.addEventListener('click', handleLogin);
passwordInput.addEventListener('keydown', (e) => {
  if (e.key === 'Enter') handleLogin();
});

backBtn.addEventListener('click', () => {
  if (!currentData) return;
  if (currentData.parent) {
    loadPath(currentData.parent);
  } else if (currentData.isRoot) {
    // 已经是根目录
    showToast('已经在根目录');
  } else {
    loadPath('');
  }
});

refreshBtn.addEventListener('click', () => {
  loadPath(currentPath);
  showToast('已刷新');
});

shutdownBtn.addEventListener('click', async () => {
  const confirmed = window.confirm('确定要关闭电脑上的文件服务器吗？关闭后手机将无法继续访问。');
  if (!confirmed) return;

  shutdownBtn.disabled = true;
  try {
    await apiFetch('/api/shutdown', { method: 'POST' });
    showToast('服务器正在关闭');
  } catch (e) {
    shutdownBtn.disabled = false;
    showToast(e.message || '关闭服务器失败');
  }
});

logoutBtn.addEventListener('click', async () => {
  try {
    await apiFetch('/api/logout', { method: 'POST' });
  } catch (e) {
    // 忽略错误，无论成功与否都退出
  }
  currentToken = null;
  localStorage.removeItem(TOKEN_KEY);
  showLoginScreen();
  showToast('已退出登录');
});

// 图片查看器：屏幕左右三分之一分别切换上一张和下一张，中间区域保持查看
imageFullscreenBtn.addEventListener('click', (e) => {
  e.stopPropagation();
  toggleImageFullscreen();
});

document.addEventListener('fullscreenchange', updateFullscreenButton);
document.addEventListener('webkitfullscreenchange', updateFullscreenButton);

imagePrevBtn.addEventListener('click', (e) => {
  e.stopPropagation();
  showPreviousImage();
});

imageNextBtn.addEventListener('click', (e) => {
  e.stopPropagation();
  showNextImage();
});

imageViewer.addEventListener('click', (e) => {
  // 忽略滑动结束后浏览器合成的点击事件，避免误触发关闭或切换。
  if (imageSwipeDetected) {
    imageSwipeDetected = false;
    return;
  }

  if (e.target === imageViewer) {
    const ratio = e.clientX / imageViewer.clientWidth;
    if (ratio < 1 / 3) {
      showPreviousImage();
    } else if (ratio > 2 / 3) {
      showNextImage();
    } else {
      closeViewer();
    }
  }
});

imageViewer.addEventListener('touchstart', (e) => {
  if (e.touches.length !== 1) return;
  imageTouchStartX = e.touches[0].clientX;
  imageTouchStartY = e.touches[0].clientY;
  imageSwipeDetected = false;
}, { passive: true });

imageViewer.addEventListener('touchend', (e) => {
  if (e.changedTouches.length !== 1) return;

  const touch = e.changedTouches[0];
  const deltaX = touch.clientX - imageTouchStartX;
  const deltaY = touch.clientY - imageTouchStartY;
  const swipeThreshold = 48;

  // 只响应明显的水平滑动，避免影响垂直触摸和普通点击。
  if (Math.abs(deltaX) >= swipeThreshold && Math.abs(deltaX) > Math.abs(deltaY)) {
    imageSwipeDetected = true;
    if (deltaX > 0) {
      showPreviousImage();
    } else {
      showNextImage();
    }
  }
}, { passive: true });

playerVideo.addEventListener('webkitbeginfullscreen', () => {
  videoPlayer.classList.add('native-fullscreen');
});

playerVideo.addEventListener('webkitendfullscreen', () => {
  videoPlayer.classList.remove('native-fullscreen');
});

// 按返回键退出媒体预览
window.addEventListener('popstate', () => {
  if (!imageViewer.classList.contains('hidden') || !videoPlayer.classList.contains('hidden')) {
    closeViewer();
  }
});

// ============================================================
//  启动
// ============================================================

init();
