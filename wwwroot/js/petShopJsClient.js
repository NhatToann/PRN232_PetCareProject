/*
 * LS-10 — JavaScript client demo (slide PRN232 Ch07).
 *
 * Slide Ch07 yêu cầu demo đủ 4 cách gọi HTTP từ JavaScript:
 *   1. jQuery $.ajax()      — request tuỳ biến (method, url, data, contentType)
 *   2. jQuery $.get()       — GET rút gọn
 *   3. jQuery $.post()      — POST rút gọn
 *   4. XMLHttpRequest       — API gốc của trình duyệt (không dùng jQuery)
 *
 * Mọi lời gọi đều trỏ tới API thật của ứng dụng:
 *   - api/products-temp/*  (LS-04 — CRUD đầy đủ: create / update / delete / search)
 *   - api/products         (API GET chuẩn)
 *
 * File này được nạp trong Components/App.razor, KHÔNG nhúng <script> trong file .razor.
 */
window.petShopJsClient = (function () {
    'use strict';

    // Base URL của API — dùng đường dẫn tương đối để chạy được ở mọi host/port.
    var apiBase = '/api';

    /**
     * Ghi log lỗi ra console kèm ngữ cảnh để tiện tra cứu.
     * @param {string} context Mô tả thao tác đang thực hiện.
     * @param {any} error Đối tượng lỗi.
     */
    function logError(context, error) {
        // eslint-disable-next-line no-console
        console.error('[petShopJsClient] ' + context + ' thất bại:', error);
    }

    /**
     * Đọc body JSON của response, trả về null nếu body rỗng.
     * @param {Response} response Response gốc.
     * @returns {Promise<any>} Nội dung JSON hoặc null.
     */
    async function readJson(response) {
        var text = await response.text();
        if (!text) {
            return null;
        }

        try {
            return JSON.parse(text);
        } catch (e) {
            logError('Đọc JSON', e);
            return null;
        }
    }

    /**
     * Dựng thông báo lỗi có thông tin cho người dùng (không nuốt lỗi).
     * @param {string} context Mô tả thao tác.
     * @param {Response} response Response gốc.
     * @param {any} payload Nội dung body đã parse (có thể null).
     * @returns {string} Thông báo lỗi.
     */
    function buildErrorMessage(context, response, payload) {
        var detail = '';

        if (payload && payload.message) {
            detail = payload.message;
        } else if (payload && payload.title) {
            detail = payload.title;
        } else if (payload && payload.errors) {
            detail = JSON.stringify(payload.errors);
        }

        var suffix = detail ? ' — ' + detail : '';

        return context + ' thất bại (HTTP ' + response.status + ')' + suffix;
    }

    // ---------------------------------------------------------------------
    // 1) jQuery $.ajax() — dùng cho CRUD POST (create / update / delete).
    // ---------------------------------------------------------------------

    /**
     * Gọi API bằng $.ajax() với method và body tuỳ biến.
     * @param {string} method HTTP method (GET/POST/...).
     * @param {string} url Đường dẫn API (bắt đầu bằng /).
     * @param {object|null} data Dữ liệu gửi kèm (object sẽ được stringify thành JSON).
     * @returns {Promise<any>} Promise chứa kết quả JSON.
     */
    function ajax(method, url, data) {
        return new Promise(function (resolve, reject) {
            var settings = {
                url: apiBase + url,
                method: method,
                dataType: 'json',
                contentType: 'application/json; charset=utf-8'
            };

            if (data !== null && data !== undefined) {
                settings.data = JSON.stringify(data);
            }

            $.ajax(settings)
                .done(function (payload) {
                    resolve(payload);
                })
                .fail(function (xhr, textStatus, errorThrown) {
                    var message = contextMessage(method, url, xhr, textStatus, errorThrown);
                    logError(message, xhr);
                    reject(new Error(message));
                });
        });
    }

    /**
     * Tạo thông báo lỗi từ thông tin của $.ajax fail callback.
     * @param {string} method HTTP method.
     * @param {string} url Đường dẫn API.
     * @param {any} xhr Đối tượng jqXHR.
     * @param {string} textStatus Trạng thái text.
     * @param {string} errorThrown Nội dung lỗi.
     * @returns {string} Thông báo lỗi.
     */
    function contextMessage(method, url, xhr, textStatus, errorThrown) {
        var body = '';

        if (xhr && xhr.responseJSON && xhr.responseJSON.message) {
            body = ' — ' + xhr.responseJSON.message;
        } else if (errorThrown) {
            body = ' — ' + errorThrown;
        }

        return method + ' ' + url + ' thất bại (' + textStatus + ')' + body;
    }

    /**
     * Tạo sản phẩm mới — POST api/products-temp/create.
     * @param {object} product Dữ liệu sản phẩm.
     * @returns {Promise<any>} Kết quả từ server.
     */
    function createProduct(product) {
        return ajax('POST', '/products-temp/create', product);
    }

    /**
     * Cập nhật sản phẩm — POST api/products-temp/update.
     * @param {object} product Dữ liệu sản phẩm (phải có productId).
     * @returns {Promise<any>} Kết quả từ server.
     */
    function updateProduct(product) {
        return ajax('POST', '/products-temp/update', product);
    }

    /**
     * Xoá mềm sản phẩm — POST api/products-temp/delete.
     * @param {number} productId Mã sản phẩm.
     * @returns {Promise<any>} Kết quả từ server.
     */
    function deleteProduct(productId) {
        return ajax('POST', '/products-temp/delete', { productId: productId });
    }

    // ---------------------------------------------------------------------
    // 2) jQuery $.get() — dùng để đọc danh sách sản phẩm.
    // ---------------------------------------------------------------------

    /**
     * Lấy danh sách sản phẩm bằng $.get().
     * @param {string} keyword Từ khoá lọc (có thể rỗng).
     * @param {number|null} categoryId Mã phân loại (null = tất cả).
     * @returns {Promise<any>} Danh sách sản phẩm.
     */
    function getProducts(keyword, categoryId) {
        return new Promise(function (resolve, reject) {
            var query = $.param({ keyword: keyword || '', categoryId: categoryId || '' });

            $.get(apiBase + '/products-temp/search?' + query)
                .done(function (payload) {
                    resolve(payload);
                })
                .fail(function (xhr, textStatus, errorThrown) {
                    var message = 'GET /products-temp/search thất bại (' + textStatus + ') — ' + errorThrown;
                    logError(message, xhr);
                    reject(new Error(message));
                });
        });
    }

    /**
     * Lấy danh sách phân loại bằng $.get() (API GET chuẩn api/products/categories).
     * @returns {Promise<any>} Danh sách phân loại.
     */
    function getCategories() {
        return new Promise(function (resolve, reject) {
            $.get(apiBase + '/products/categories')
                .done(function (payload) {
                    resolve(payload);
                })
                .fail(function (xhr, textStatus, errorThrown) {
                    var message = 'GET /products/categories thất bại (' + textStatus + ') — ' + errorThrown;
                    logError(message, xhr);
                    reject(new Error(message));
                });
        });
    }

    // ---------------------------------------------------------------------
    // 3) jQuery $.post() — dùng cho thao tác ghi đơn giản không cần body tùy biến.
    // ---------------------------------------------------------------------

    /**
     * Gọi POST đơn giản bằng $.post() — dùng cho khôi phục sản phẩm đã xoá mềm.
     *
     * <para>
     * Mặc định $.post() gửi body dạng <c>application/x-www-form-urlencoded</c> nên API
     * ([FromBody]) sẽ trả 415 Unsupported Media Type. Vì vậy dùng $.ajax() với
     * <c>type: 'POST'</c> + <c>contentType: 'application/json'</c> để gửi JSON — đây là
     * biến thể "$.post()" của jQuery ($.post() bản thân là wrapper của $.ajax type POST).
     * </para>
     * @param {string} url Đường dẫn API.
     * @param {object} data Dữ liệu gửi kèm.
     * @returns {Promise<any>} Kết quả từ server.
     */
    function post(url, data) {
        return new Promise(function (resolve, reject) {
            $.ajax({
                url: apiBase + url,
                type: 'POST',
                dataType: 'json',
                contentType: 'application/json; charset=utf-8',
                data: JSON.stringify(data)
            })
                .done(function (payload) {
                    resolve(payload);
                })
                .fail(function (xhr, textStatus, errorThrown) {
                    var body = xhr && xhr.responseJSON && xhr.responseJSON.message
                        ? ' — ' + xhr.responseJSON.message
                        : ' — ' + errorThrown;
                    var message = 'POST ' + url + ' thất bại (' + textStatus + ')' + body;
                    logError(message, xhr);
                    reject(new Error(message));
                });
        });
    }

    /**
     * Khôi phục sản phẩm đã xoá mềm — POST api/products-temp/restore.
     * @param {number} productId Mã sản phẩm.
     * @returns {Promise<any>} Kết quả từ server.
     */
    function restoreProduct(productId) {
        return post('/products-temp/restore', { productId: productId });
    }

    /**
     * Xoá vĩnh viễn sản phẩm bằng $.post() — demo $.post() với body JSON.
     * @param {number} productId Mã sản phẩm.
     * @returns {Promise<any>} Kết quả từ server.
     */
    function permanentDeleteWithPost(productId) {
        return post('/products-temp/permanent-delete', { productId: productId });
    }

    // ---------------------------------------------------------------------
    // 4) XMLHttpRequest — API gốc của trình duyệt, không dùng jQuery.
    // ---------------------------------------------------------------------

    /**
     * Gọi API bằng XMLHttpRequest thuần (slide Ch07 S27/S30 — "Using JavaScript XMLHttpRequest").
     * @param {string} method HTTP method.
     * @param {string} url Đường dẫn API (bắt đầu bằng /).
     * @param {object|null} data Dữ liệu JSON gửi kèm (null = không có body).
     * @returns {Promise<any>} Kết quả JSON.
     */
    function xhrRequest(method, url, data) {
        return new Promise(function (resolve, reject) {
            var request = new XMLHttpRequest();
            request.open(method, apiBase + url, true);
            request.setRequestHeader('Accept', 'application/json');

            if (data !== null && data !== undefined) {
                request.setRequestHeader('Content-Type', 'application/json; charset=utf-8');
            }

            request.onload = function () {
                var payload = null;

                if (request.responseText) {
                    try {
                        payload = JSON.parse(request.responseText);
                    } catch (e) {
                        logError('Parse JSON (XMLHttpRequest)', e);
                    }
                }

                if (request.status >= 200 && request.status < 300) {
                    resolve(payload);
                    return;
                }

                var detail = payload && payload.message ? ' — ' + payload.message : '';
                var message = method + ' ' + url + ' thất bại (HTTP ' + request.status + ')' + detail;

                logError(message, request);
                reject(new Error(message));
            };

            request.onerror = function () {
                var message = method + ' ' + url + ' thất bại — lỗi mạng (XMLHttpRequest).';
                logError(message, request);
                reject(new Error(message));
            };

            request.send(data !== null && data !== undefined ? JSON.stringify(data) : null);
        });
    }

    /**
     * Đọc chi tiết một sản phẩm bằng XMLHttpRequest (GET api/products/{id}).
     * @param {number} productId Mã sản phẩm.
     * @returns {Promise<any>} Thông tin sản phẩm.
     */
    function getProductById(productId) {
        return xhrRequest('GET', '/products/' + productId, null);
    }

    /**
     * Tải ảnh sản phẩm bằng XMLHttpRequest dạng blob — demo XHR ở dạng binary.
     * @param {string} imageUrl Đường dẫn ảnh (bắt đầu bằng /).
     * @returns {Promise<string>} Object URL của blob ảnh.
     */
    function loadImageBlobUrl(imageUrl) {
        return new Promise(function (resolve, reject) {
            var request = new XMLHttpRequest();
            request.open('GET', imageUrl, true);
            request.responseType = 'blob';

            request.onload = function () {
                if (request.status >= 200 && request.status < 300) {
                    resolve(URL.createObjectURL(request.response));
                    return;
                }

                var message = 'Tải ảnh ' + imageUrl + ' thất bại (HTTP ' + request.status + ').';
                logError(message, request);
                reject(new Error(message));
            };

            request.onerror = function () {
                var message = 'Tải ảnh ' + imageUrl + ' thất bại — lỗi mạng (XMLHttpRequest).';
                logError(message, request);
                reject(new Error(message));
            };

            request.send();
        });
    }

    /**
     * Xoá vĩnh viễn sản phẩm bằng XMLHttpRequest POST (demo XHR với body).
     * @param {number} productId Mã sản phẩm.
     * @returns {Promise<any>} Kết quả từ server.
     */
    function permanentDeleteProduct(productId) {
        return xhrRequest('POST', '/products-temp/permanent-delete', { productId: productId });
    }

    return {
        apiBase: apiBase,
        ajax: ajax,
        get: getProducts,
        getCategories: getCategories,
        post: post,
        postPermanentDelete: permanentDeleteWithPost,
        xhrRequest: xhrRequest,
        createProduct: createProduct,
        updateProduct: updateProduct,
        deleteProduct: deleteProduct,
        restoreProduct: restoreProduct,
        getProductById: getProductById,
        loadImageBlobUrl: loadImageBlobUrl,
        permanentDeleteProduct: permanentDeleteProduct
    };
})();
