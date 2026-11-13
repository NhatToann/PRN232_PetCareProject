// blazorAuth.js — Google Identity Services helpers for Blazor
window.blazorAuth = (function () {
    let _dotNetRef = null;
    let _codeRef = null;
    let _pendingState = null;
    let _gsiDone = false;
    let _gsiTimer = null;

    function initGoogle(clientId, dotNetRef) {
        _dotNetRef = dotNetRef;

        function setup() {
            if (!window.google?.accounts?.id) return;
            window.google.accounts.id.initialize({
                client_id: clientId,
                callback: function (response) {
                    const token = response?.credential ?? '';
                    if (!token) return;
                    _gsiDone = true;
                    if (_gsiTimer) { clearTimeout(_gsiTimer); _gsiTimer = null; }
                    _dotNetRef.invokeMethodAsync('OnGoogleCredential', token);
                }
            });
        }

        const SCRIPT_ID = 'google-identity-services-script';
        const existing = document.getElementById(SCRIPT_ID);
        if (existing) {
            if (window.google?.accounts?.id) setup();
            else existing.addEventListener('load', setup, { once: true });
            return;
        }

        const script = document.createElement('script');
        script.id = SCRIPT_ID;
        script.src = 'https://accounts.google.com/gsi/client';
        script.async = true;
        script.defer = true;
        script.onload = setup;
        document.head.appendChild(script);
    }

    // -------------------------------------------------------------------------
    // GSI prompt() — luồng chính (giống 100% bản gốc OJT).
    //
    // Nếu origin hiện tại KHÔNG nằm trong `javascript_origins` của OAuth client,
    // GSI sẽ log "[GSI_LOGGER]: The given origin is not allowed for the given client ID."
    // và không mở popup. Sau 2.5s không nhận được credential ⇒ báo cho .NET
    // chuyển sang luồng fallback (Authorization Code flow).
    // -------------------------------------------------------------------------
    function promptGoogle() {
        if (window.google?.accounts?.id) {
            _gsiDone = false;
            window.google.accounts.id.prompt();

            if (_gsiTimer) clearTimeout(_gsiTimer);
            _gsiTimer = setTimeout(function () {
                _gsiTimer = null;
                if (_gsiDone) return;
                if (_dotNetRef) {
                    _dotNetRef.invokeMethodAsync('OnGoogleFallbackNeeded');
                }
            }, 2500);
        }
    }

    // -------------------------------------------------------------------------
    // Fallback: OAuth 2.0 Authorization Code flow.
    //
    // GSI chỉ hoạt động từ origin nằm trong `javascript_origins` của OAuth client
    // (vd client chỉ đăng ký http://localhost:5286 ⇒ mở app ở
    // https://localhost:7083 sẽ bị GSI chặn). Flow code chỉ phụ thuộc
    // `redirect_uris` ⇒ hoạt động từ mọi origin/port.
    //
    // Luồng:
    //   1. Opener mở popup → authorizeUrl (redirect_uri = http://localhost:5286)
    //   2. Google redirect popup về http://localhost:5286/?code=...&state=...
    //   3. Popup (chạy cùng app) đọc `code`, POST /api/auth/google-code-login
    //   4. Popup postMessage `code` về opener (targetOrigin lấy từ `state`)
    //   5. Opener nhận code ⇒ .NET đổi code lấy token ⇒ đăng nhập
    // -------------------------------------------------------------------------

    function setCodeRef(ref) {
        _codeRef = ref;
    }

    /** Mở popup Google OAuth. `authorizeUrl` do server sinh (có sẵn state). */
    function openGoogleAuthPopup(authorizeUrl) {
        const width = 520;
        const height = 640;
        const left = Math.max(0, Math.round((window.screen.width - width) / 2));
        const top = Math.max(0, Math.round((window.screen.height - height) / 2));

        const popup = window.open(
            authorizeUrl,
            'google-oauth-popup',
            `width=${width},height=${height},left=${left},top=${top},resizable=yes,scrollbars=yes`
        );

        if (!popup) {
            if (_dotNetRef) {
                _dotNetRef.invokeMethodAsync('OnGoogleCodeError', 'popup_blocked');
            }
            return;
        }

        // Nếu user đóng popup ⇒ không có code ⇒ báo lỗi sau một khoảng thời gian.
        const poll = setInterval(function () {
            if (popup.closed) {
                clearInterval(poll);
                if (_codeRef && !_codeRef.__resolved) {
                    _codeRef.__resolved = true;
                    _codeRef.invokeMethodAsync('OnGoogleCodeError', 'popup_closed');
                }
            }
        }, 700);
    }

    /** Tạo state chứa origin của opener (để popup postMessage đúng targetOrigin). */
    function buildState(origin) {
        _pendingState = origin;
        try {
            return btoa(unescape(encodeURIComponent(JSON.stringify({ o: origin || '' }))));
        } catch {
            return '';
        }
    }

    /**
     * Xử lý khi trang hiện tại là popup callback (có ?code= hoặc ?error=).
     * Gửi code về opener qua postMessage.
     */
    function handleOAuthCallback() {
        const params = new URLSearchParams(window.location.search);
        const code = params.get('code');
        const error = params.get('error');
        const state = params.get('state') || '';

        // QUAN TRỌNG — phải có `state` mới coi là callback Google OAuth.
        //
        // VÌ SAO CẦN:
        // Google LUÔN trả về `state` (echo lại state mà ta gửi khi mở popup).
        // Còn các hệ thống thanh toán bên ngoài — ví dụ PayOS — cũng gắn thêm
        // tham số `code` vào returnUrl:
        //     /spa-invoice?payos=success&bookingId=1398&...&code=00&orderCode=...
        // Nếu coi mọi `?code=` là callback OAuth thì đoạn `history.replaceState`
        // bên dưới sẽ XOÁ SẠCH toàn bộ query string ⇒ mất `bookingId`/`orderCode`
        // ⇒ trang hóa đơn báo "Thiếu mã booking" dù khách đã thanh toán thành công.
        if (!state) return;

        if (!code && !error) return;

        let openerOrigin = '';
        try {
            if (state) {
                const decoded = JSON.parse(decodeURIComponent(escape(atob(state))));
                openerOrigin = decoded.o || '';
            }
        } catch {
            openerOrigin = '';
        }

        if (!openerOrigin) {
            // Fallback: lấy origin từ referrer.
            try {
                if (document.referrer) openerOrigin = new URL(document.referrer).origin;
            } catch {
                openerOrigin = '';
            }
        }

        // Dọn query khỏi thanh URL để không refresh lại callback.
        try {
            window.history.replaceState({}, document.title, window.location.pathname);
        } catch { /* bỏ qua */ }

        if (!window.opener) {
            if (error) {
                document.body.innerHTML = '<p style="font-family:sans-serif;padding:24px">'
                    + 'Đăng nhập Google thất bại: ' + error + '</p>';
            }
            return;
        }

        const payload = error
            ? { type: 'petshop-google-oauth', error: error }
            : { type: 'petshop-google-oauth', code: code, state: state };

        window.opener.postMessage(payload, openerOrigin || '*');
    }

    // Nhận code từ popup (opener side) ⇒ chuyển cho .NET.
    window.addEventListener('message', function (event) {
        const data = event.data;
        if (!data || data.type !== 'petshop-google-oauth') return;

        if (data.error) {
            if (_codeRef) {
                _codeRef.__resolved = true;
                _codeRef.invokeMethodAsync('OnGoogleCodeError', data.error);
            }
            return;
        }

        if (_codeRef) {
            _codeRef.__resolved = true;
            _codeRef.invokeMethodAsync('OnGoogleCode', data.code || '', data.state || '');
        }
    });

    // Chạy ngay khi script nạp (popup callback chạy cùng app).
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', handleOAuthCallback);
    } else {
        handleOAuthCallback();
    }

    return {
        initGoogle,
        promptGoogle,
        setCodeRef,
        openGoogleAuthPopup,
        buildState
    };
})();
