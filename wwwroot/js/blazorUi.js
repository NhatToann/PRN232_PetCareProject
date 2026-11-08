// blazorUi.js — Tiện ích UI dùng chung cho Blazor (JS interop).
//
// NGUỒN GỐC: hành vi được port từ nguồn OJT
//   `Projectojt/frontend/src/pages/CheckoutPage.jsx`:
//     useEffect(() => {
//       document.body.style.overflow = showAddressModal ? 'hidden' : ''
//       return () => { document.body.style.overflow = '' }
//     }, [showAddressModal])
//
// Mục đích: khoá cuộn trang khi mở modal (AddressModal) để trải nghiệm
// giống hệt bản gốc React.

window.blazorUi = (function () {
  // Giữ trạng thái overflow trước khi khoá để khôi phục đúng khi mở nhiều modal.
  let previousOverflow = null;

  /**
   * Bật/tắt khoá cuộn trang.
   * @param {boolean} locked true = khoá (overflow: hidden), false = nhả.
   */
  function setBodyScrollLock(locked) {
    if (!document.body) return;

    if (locked) {
      if (previousOverflow === null) {
        previousOverflow = document.body.style.overflow || '';
      }
      document.body.style.overflow = 'hidden';
      return;
    }

    document.body.style.overflow = previousOverflow === null ? '' : previousOverflow;
    previousOverflow = null;
  }

  return { setBodyScrollLock };
})();
