// blazorReconnect.js — Tự phục hồi khi circuit Blazor Server bị đóng.
//
// VÌ SAO CẦN (fix bug 10/11/2026 — "Connection disconnected"):
//
//   Khi circuit Blazor Server bị server chủ động đóng (ví dụ: hàng đợi render
//   batch đầy, hoặc circuit hết thời gian giữ), toàn bộ UI trở nên "chết":
//   mọi nút bấm không phản hồi, không có lỗi hiển thị ra màn hình.
//   Mặc định, .NET chỉ hiện modal "Could not reconnect to the server. Reload the
//   page to restore functionality." ⇒ buộc USER phải tự bấm "Reload".
//
//   Script này thay thế modal đó bằng hành vi TỰ ĐỘNG reload trang, kèm thông
//   báo ngắn gọn cho người dùng. Giữ nguyên state quan trọng trong
//   localStorage/sessionStorage nên không mất giỏ hàng hay địa chỉ đã nhập.
//
//   KỸ THUẬT:
//   .NET 8 expose Blazor.events / Blazor.internal nhưng phụ thuộc phiên bản.
//   Cách ổn định & không phụ thuộc là theo dõi trực tiếp DOM của modal reconnect
//   do chính blazor.web.js render (#components-reconnect-modal) và #blazor-error-ui.
//
//   - Khi modal reconnect xuất hiện ⇒ reload trang sau một nhịp ngắn (để Blazor
//     kịp thử reconnect nếu circuit còn trong bộ nhớ đệm).
//   - Nếu sau lần reload vẫn lỗi ⇒ vòng lặp reload sẽ dừng nhờ cơ chế
//     sessionStorage đánh dấu số lần đã thử, tránh reload vô hạn.

(function () {
  'use strict';

  var RELOAD_KEY = 'petshop.blazor.reconnect.attempts';
  var MAX_ATTEMPTS = 3;          // tối đa số lần reload tự động
  var FIRST_DELAY_MS = 1500;     // nhịp chờ lần đầu (cho Blazor kịp reconnect)
  var NEXT_DELAY_MS = 4000;      // nhịp chờ các lần sau
  var CHECK_DEBOUNCE_MS = 500;   // gom nhiều lần kiểm tra sát nhau

  if (window.__petshopReconnectInstalled) {
    return;
  }
  window.__petshopReconnectInstalled = true;

  // ⚠️ QUAN TRỌNG — chỉ được SCHEDULE MỘT lần reload tại một thời điểm.
  //
  // BUG ĐÃ GẶP (10/11/2026): MutationObserver theo dõi CẢ thuộc tính (attributes)
  // trên toàn bộ cây DOM ⇒ mỗi lần trang render lại là check() chạy vài chục lần.
  // Mỗi lần check() lại tăng bộ đếm attempts + schedule 1 lần reload ⇒ chỉ trong
  // vài trăm ms là đủ MAX_ATTEMPTS ⇒ script bỏ cuộc ⇒ trang chết hẳn, người
  // dùng buộc phải F5 (đúng triệu chứng "bạn đã làm hỏng project của tôi").
  //
  // ⇒ Dùng cờ `reloadScheduled` để chặn lịch reload trùng lặp.
  var reloadScheduled = false;
  var lastCheckAt = 0;

  function readAttempts() {
    try {
      return parseInt(sessionStorage.getItem(RELOAD_KEY) || '0', 10) || 0;
    } catch (e) {
      return 0;
    }
  }

  function writeAttempts(value) {
    try {
      sessionStorage.setItem(RELOAD_KEY, String(value));
    } catch (e) {
      /* storage bị chặn — bỏ qua */
    }
  }

  function clearAttempts() {
    try {
      sessionStorage.removeItem(RELOAD_KEY);
    } catch (e) {
      /* ignore */
    }
  }

  function showToast(message) {
    try {
      var toast = document.createElement('div');
      toast.className = 'toast-notification';
      toast.textContent = message;
      document.body.appendChild(toast);
      setTimeout(function () {
        if (toast.parentNode) {
          toast.parentNode.removeChild(toast);
        }
      }, 3000);
    } catch (e) {
      /* ignore */
    }
  }

  function scheduleReload(delay) {
    if (reloadScheduled) {
      return;
    }
    reloadScheduled = true;
    showToast('Mất kết nối với server — đang tải lại trang…');
    setTimeout(function () {
      try {
        window.location.reload();
      } catch (e) {
        /* ignore */
      }
    }, delay);
  }

  // Khi circuit kết nối lại thành công ⇒ xoá bộ đếm để lần lỗi sau được
  // thử lại từ đầu.
  function resetOnHealthyConnection() {
    if (window.Blazor && typeof window.Blazor === 'object') {
      // Blazor Server tự phát sự kiện 'reconnected' trên document khi
      // SignalR re-negotiate thành công (blazor.web.js >= 6).
      document.addEventListener('blazor.reconnected', clearAttempts, true);
      document.addEventListener('blazor.resumed', clearAttempts, true);
    }
  }

  function isVisible(el) {
    if (!el) return false;
    var style = window.getComputedStyle(el);
    return style.display !== 'none' && style.visibility !== 'hidden' && style.opacity !== '0';
  }

  function check() {
    // Chặn gọi lại quá dày (MutationObserver bắn rất nhiều event khi DOM đổi).
    var now = Date.now();
    if (now - lastCheckAt < CHECK_DEBOUNCE_MS) {
      return;
    }
    lastCheckAt = now;

    var modal = document.getElementById('components-reconnect-modal');
    var errorUi = document.getElementById('blazor-error-ui');

    var modalVisible = isVisible(modal);
    var errorVisible = isVisible(errorUi);

    if (!modalVisible && !errorVisible) {
      // Modal biến mất ⇒ nhiều khả năng circuit đã kết nối lại.
      clearAttempts();
      reloadScheduled = false;
      return;
    }

    // Đã đang chờ reload rồi ⇒ không tăng bộ đếm / không lên lịch thêm.
    if (reloadScheduled) {
      return;
    }

    // Modal reconnect (chưa kết nối lại được) ⇒ reload.
    if (modalVisible) {
      var attempts = readAttempts();
      if (attempts >= MAX_ATTEMPTS) {
        // Đã thử đủ số lần ⇒ dừng reload vô hạn, để user tự xử lý.
        showToast('Không kết nối được server. Hãy tải lại trang thủ công.');
        return;
      }

      writeAttempts(attempts + 1);
      scheduleReload(attempts === 0 ? FIRST_DELAY_MS : NEXT_DELAY_MS);
      return;
    }

    // blazor-error-ui hiện ⇒ lỗi nghiêm trọng đã xảy ra ⇒ reload 1 lần.
    var attempts = readAttempts();
    if (attempts >= MAX_ATTEMPTS) {
      return;
    }

    writeAttempts(attempts + 1);
    scheduleReload(attempts === 0 ? FIRST_DELAY_MS : NEXT_DELAY_MS);
  }

  resetOnHealthyConnection();

  // Theo dõi bằng MutationObserver (phản ứng nhanh khi modal được thêm vào DOM)
  // + interval dự phòng (trường hợp modal đã có sẵn từ đầu).
  function startObserving() {
    if (window.MutationObserver) {
      var observer = new MutationObserver(function () {
        check();
      });
      observer.observe(document.documentElement, {
        childList: true,
        subtree: true,
        // CHỈ theo dõi thuộc tính của chính modal/error-ui, KHÔNG theo dõi
        // mọi thuộc tính của mọi phần tử trong trang.
        //
        // SỬA BUG (11/11/2026): `attributes: true` + `subtree: true` khiến
        // observer bắn mỗi lần Blazor cập nhật class/style của bất kỳ phần tử
        // nào ⇒ hàng trăm event mỗi lần render ⇒ check() chạy liên tục.
        // Dù chặn bằng `reloadScheduled`, việc theo dõi cả cây DOM vẫn gây
        // tốn CPU. Chỉ cần biết khi 2 phần tử này xuất hiện / đổi hiển thị.
        attributes: true,
        attributeFilter: ['class', 'style'],
        attributeOldValue: false
      });
    }

    window.setInterval(check, 2000);
  }

  function init() {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', startObserving);
    } else {
      startObserving();
    }
  }

  init();
})();
