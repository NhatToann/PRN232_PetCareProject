/*
 * LS-10 — Cầu nối JS interop cho trang demo JavaScript client (slide PRN232 Ch07).
 *
 * Blazor không nhúng <script> trong file .razor; script được nạp ở Components/App.razor
 * và gọi vào từ đây qua IJSRuntime.InvokeVoidAsync("petShopJsClient.xxx").
 */

/**
 * Gọi hàm trong window.petShopJsClient.
 * @param {string} method Tên hàm cần gọi.
 * @param {Array} args Mảng tham số truyền vào hàm.
 * @param {string} elementId Id phần tử HTML cần cập nhật (có thể rỗng).
 * @returns {Promise<any>} Kết quả trả về từ hàm JS.
 */
async function invoke(method, args, elementId) {
    const fn = window.petShopJsClient && window.petShopJsClient[method];

    if (!fn) {
        throw new Error('Không tìm thấy hàm "' + method + '" trong petShopJsClient. ' +
            'Hãy kiểm tra file wwwroot/js/petShopJsClient.js đã được nạp trong App.razor.');
    }

    const result = await fn.apply(window.petShopJsClient, args || []);

    if (elementId) {
        renderRows(elementId, result);
    }

    return result;
}

/**
 * Render danh sách sản phẩm (dùng jQuery selector) vào bảng HTML.
 * @param {string} elementId Id của tbody.
 * @param {Array} products Danh sách sản phẩm.
 */
function renderRows(elementId, products) {
    const $body = $('#' + elementId);

    if ($body.length === 0) {
        return;
    }

    $body.empty();

    if (!products || products.length === 0) {
        $body.append(
            '<tr><td colspan="6" class="text-center text-muted py-3">Không có sản phẩm nào.</td></tr>');
        return;
    }

    products.forEach(function (p) {
        const price = Number(p.price || 0).toLocaleString('vi-VN');
        const description = p.description ? String(p.description) : '';
        const image = p.imageUrl
            ? '<img src="' + p.imageUrl + '" alt="" style="width:38px;height:38px;object-fit:cover;border-radius:8px;" />'
            : '<span class="text-muted">—</span>';

        const row = $('<tr></tr>');
        row.append($('<td></td>').text(p.productId));
        row.append($('<td></td>').append(image));
        row.append($('<td class="fw-semibold"></td>').text(p.name));
        row.append($('<td></td>').text(price + ' đ'));
        row.append($('<td></td>').text(p.stockQuantity));
        row.append($('<td class="small text-muted"></td>').text(description));
        $body.append(row);
    });
}

/**
 * Hiển thị thông báo trạng thái lên phần tử có id cho trước.
 * @param {string} elementId Id phần tử.
 * @param {string} message Nội dung thông báo.
 * @param {string} level Mức độ: 'success' | 'danger' | 'info'.
 */
function setStatus(elementId, message, level) {
    const $el = $('#' + elementId);

    if ($el.length === 0) {
        return;
    }

    $el.removeClass('alert-success alert-danger alert-info alert-warning')
        .addClass('alert-' + (level || 'info'))
        .text(message)
        .toggleClass('d-none', !message);
}
