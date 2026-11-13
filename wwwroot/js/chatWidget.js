/*
 * chatWidget.js — tiện ích JS cho hộp thoại hợp nhất (ChatWidget).
 *
 * VÌ SAO CẦN:
 *   Blazor Server không tự cuộn khung chat xuống đáy khi có tin nhắn mới
 *   ⇒ người dùng phải tự kéo thanh cuộn. Hàm này cuộn tới vị trí cuối
 *   một cách mượt (smooth) sau khi DOM đã render.
 *
 * Nạp tập trung tại Components/App.razor (không nhúng <script> trong .razor).
 */
window.chatWidget = (function () {
  /**
   * Cuộn một phần tử tới đáy.
   *
   * ⚠️ RẤT QUAN TRỌNG — không dựa vào `requestAnimationFrame` / smooth-scroll:
   *   Khi tab trình duyệt bị ẩn (background) hoặc khi `prefers-reduced-motion`
   *   bật, `requestAnimationFrame` KHÔNG chạy và animation scroll bị huỷ
   *   ⇒ khung chat mắc kẹt ở vị trí cũ, người dùng không thấy tin mới.
   *   ⇒ Vì vậy: gán `scrollTop` trực tiếp (luôn chạy, tức thì) là cách
   *     chắc chắn nhất; phần "mượt" chỉ là lớp trang trí, không phải điều kiện.
   *
   * @param {string} elementId Id phần tử (ví dụ "chatMessagesBox").
   * @param {boolean} smooth Có dùng hiệu ứng mượt hay không (mặc định có).
   */
  function scrollToBottom(elementId, smooth) {
    if (!elementId) return;

    const el = document.getElementById(elementId);
    if (!el) return;

    const useSmooth = smooth !== false;

    // 1) Luôn cuộn tới đáy bằng cách gán trực tiếp (chắc chắn hoạt động).
    const jump = function () {
      el.scrollTop = el.scrollHeight;
    };

    // 2) Nếu trình duyệt hỗ trợ cuộn mượt thì thêm hiệu ứng (best-effort).
    if (useSmooth && typeof el.scrollTo === 'function') {
      try {
        el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' });
      } catch {
        /* bỏ qua — đã cuộn tới đáy ở bước 1 */
      }
    }

    // 3) Chờ Blazor render xong DOM rồi cuộn lại (nội dung vừa thêm vào).
    //    Dùng setTimeout thay vì requestAnimationFrame vì rAF bị treo khi
    //    tab bị ẩn ⇒ tin nhắn mới sẽ không hiện ra.
    if (typeof window.setTimeout === 'function') {
      window.setTimeout(jump, 60);
      window.setTimeout(jump, 220);
    }
  }

  /**
   * Trả về vị trí cuộn hiện tại của khung chat (px từ đáy).
   * Dùng để biết người dùng đang ở đáy hay không.
   * @param {string} elementId Id phần tử.
   * @returns {number} Khoảng cách tới đáy (0 = đang ở đáy).
   */
  function distanceFromBottom(elementId) {
    if (!elementId) return 0;

    const el = document.getElementById(elementId);
    if (!el) return 0;

    return el.scrollHeight - el.scrollTop - el.clientHeight;
  }

  return { scrollToBottom, distanceFromBottom };
})();
