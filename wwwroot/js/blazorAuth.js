// blazorAuth.js — Google Identity Services helpers for Blazor
window.blazorAuth = (function () {
    let _dotNetRef = null;
    let _tokenClient = null;
    let _buttonId = null;

    function initGoogle(clientId, dotNetRef, buttonId) {
        _dotNetRef = dotNetRef;
        _buttonId = buttonId || _buttonId;

        function setup() {
            if (window.google?.accounts?.id) {
                window.google.accounts.id.initialize({
                    client_id: clientId,
                    callback: function (response) {
                        const token = response?.credential ?? '';
                        if (!token) return;
                        _dotNetRef.invokeMethodAsync('OnGoogleCredential', token);
                    }
                });
            }

            if (window.google?.accounts?.oauth2) {
                _tokenClient = window.google.accounts.oauth2.initTokenClient({
                    client_id: clientId,
                    scope: 'openid email profile',
                    callback: function (response) {
                        const token = response?.access_token ?? '';
                        if (!token) return;
                        _dotNetRef.invokeMethodAsync('OnGoogleAccessToken', token);
                    }
                });
            }

            bindGoogleButton();
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

    function bindGoogleButton() {
        if (!_buttonId) return;

        const button = document.getElementById(_buttonId);
        if (!button || button.dataset.googleBound === 'true') return;

        button.dataset.googleBound = 'true';
        button.addEventListener('click', function (event) {
            event.preventDefault();
            const started = promptGoogle();
            if (!started) {
                notifyError('Google Login chưa sẵn sàng. Vui lòng tải lại trang và thử lại.');
            }
        });
    }

    function promptGoogle() {
        try {
            if (_tokenClient) {
                _tokenClient.requestAccessToken({ prompt: 'select_account' });
                return true;
            }

            if (window.google?.accounts?.id) {
                window.google.accounts.id.prompt();
                return true;
            }
        } catch (error) {
            notifyError(error?.message || 'Không mở được Google Login.');
        }

        return false;
    }

    function notifyError(message) {
        if (!_dotNetRef) return;
        _dotNetRef.invokeMethodAsync('OnGoogleError', message).catch(function () { });
    }

    function getStatus() {
        return {
            hasDotNetRef: !!_dotNetRef,
            hasTokenClient: !!_tokenClient,
            buttonId: _buttonId,
            buttonBound: _buttonId ? document.getElementById(_buttonId)?.dataset.googleBound === 'true' : false,
            hasGoogle: !!window.google,
            hasGoogleId: !!window.google?.accounts?.id,
            hasGoogleOauth2: !!window.google?.accounts?.oauth2
        };
    }

    return { initGoogle, promptGoogle, bindGoogleButton, getStatus };
})();
