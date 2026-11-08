// daNangData.js — Dữ liệu địa chính Đà Nẵng (8 quận/huyện + danh sách phường/xã).
//
// NGUỒN GỐC: Projectojt/frontend/src/components/checkout/daNangData.js (OJT).
// Port nguyên văn phần DỮ LIỆU (8 quận/huyện + toàn bộ phường/xã + các hàm
// normalizeText / validatePhone / validateEmail / getWardsForDistrict /
// isWardValidForDistrict / formatPhoneDisplay).
//
// KHÁC BIỆT DUY NHẤT so với nguồn (bắt buộc kỹ thuật, không đổi dữ liệu):
//   - Nguồn dùng cú pháp ESM `export const ...` ⇒ không nạp được bằng thẻ
//     `<script src="js/daNangData.js">` (không phải module). Ở đây bọc trong
//     IIFE và gắn ra `window.daNangData` để Blazor JS interop gọi được.
//   - `buildGoogleMapsEmbedSrc` đọc `import.meta.env.VITE_GOOGLE_MAPS_EMBED_API_KEY`
//     — biến môi trường của Vite, không tồn tại ngoài bundler. Ở đây đọc từ
//     `window.daNangDataConfig.googleMapsEmbedApiKey` (do host page nạp trước)
//     và dùng biến đó nếu có; nếu không có thì rơi về đúng URL fallback của nguồn.
//
// Toàn bộ chuỗi tiếng Việt trong phần dữ liệu giữ NGUYÊN VĂN.

window.daNangData = (function () {
  const DA_NANG_WARDS_BY_DISTRICT = {
    'Quận Hải Châu': [
      'Phường Thạch Thang', 'Phường Hải Châu I', 'Phường Hải Châu II',
      'Phường Phước Ninh', 'Phường Hùng Vương', 'Phường Chính Gián',
      'Phường Vĩnh Trung', 'Phường Thanh Bình', 'Phường Thuận Phước',
      'Phường Thủy Thuỷ', 'Phường Hòa Cường Bắc', 'Phường Hòa Cường Nam',
      'Phường Hòa Cường', 'Phường Nam Dương', 'Phường Ong Ích Khiêm',
    ],
    'Quận Thanh Khê': [
      'Phường An Xuân', 'Phường Tam Thuận', 'Phường Thanh Khê Tây',
      'Phường Thanh Khê Đông', 'Phường Kim Ly', 'Phường Hòa Khê',
      'Phường Xuân Hà', 'Phường Tân Chính', 'Phường Bình Hiên',
      'Phường Bình Thuận', 'Phường Hòa Cường Bắc', 'Phường Hòa Cường Nam',
    ],
    'Quận Sơn Trà': [
      'Phường Thọ Quang', 'Phường Mân Thái', 'Phường Nại Hiên Đông',
      'Phường Phước Mỹ', 'Phường An Hải Bắc', 'Phường An Hải Nam',
      'Phường An Hải Tây', 'Phường Mỹ An', 'Phường Khuê Trung',
    ],
    'Quận Ngũ Hành Sơn': [
      'Phường Mỹ An', 'Phường Khuê Mỹ', 'Phường Hoà Quý', 'Phường Hoà Hải',
      'Phường Khuê Trung',
    ],
    'Quận Liên Chiểu': [
      'Phường Hòa Khánh Bắc', 'Phường Hòa Khánh Nam', 'Phường Hòa Minh',
      'Phường Hòa Bắc', 'Phường Hòa Liên', 'Phường Hòa Nhơn',
    ],
    'Quận Cẩm Lệ': [
      'Phường Hòa An', 'Phường Hòa Phát', 'Phường Khuê Trung',
      'Phường Hòa Quý', 'Phường Hòa Khê', 'Phường Khuê Mỹ',
      'Phường Hòa Cường Bắc', 'Phường Hòa Cường Nam', 'Phường An Lạc',
    ],
    'Huyện Hòa Vang': [
      'Phường Hòa Phước', 'Phường Hòa Sơn', 'Phường Hòa Tiến',
      'Phường Hòa Khương', 'Phường Hòa Phong', 'Phường Hòa Ninh',
      'Phường Hòa Phú', 'Phường Hòa Bắc', 'Phường Hòa Liên',
      'Phường Hòa Nhơn', 'Phường Hòa Bình', 'Phường Hòa Thọ Bắc',
      'Phường Hòa Thọ Đông', 'Phường Hòa Thọ Nam', 'Xã Hòa Bắc',
      'Xã Hòa Liên', 'Xã Hòa Nhơn', 'Xã Hòa Ninh', 'Xã Hòa Phú',
      'Xã Hòa Phong', 'Xã Hòa Phước', 'Xã Hòa Sơn', 'Xã Hòa Tiến',
      'Xã Hòa Khương', 'Xã Hòa Khánh', 'Xã Hòa Bình', 'Xã Hòa Thọ Bắc',
      'Xã Hòa Thọ Đông', 'Xã Hòa Thọ Nam',
    ],
    'Huyện Hoàng Sa': [],
  };

  const DA_NANG_DISTRICTS = [
    'Quận Hải Châu',
    'Quận Thanh Khê',
    'Quận Sơn Trà',
    'Quận Ngũ Hành Sơn',
    'Quận Liên Chiểu',
    'Quận Cẩm Lệ',
    'Huyện Hòa Vang',
    'Huyện Hoàng Sa',
  ];

  const ALL_DA_NANG_WARDS = Object.values(DA_NANG_WARDS_BY_DISTRICT).flat();

  const DEFAULT_PROVINCE = 'Đà Nẵng';

  const normalizeText = (value) =>
    (value || '')
      .toLowerCase()
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .replace(/đ/g, 'd');

  const validatePhone = (phone) => {
    const cleanPhone = phone.replace(/\s/g, '');
    const phoneRegex = /^(0[0-9]{9,10})$/;
    return phoneRegex.test(cleanPhone);
  };

  const validateEmail = (email) => {
    if (!email) return true;
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    return emailRegex.test(email);
  };

  const getWardsForDistrict = (dist) => {
    if (!dist) return [];
    return DA_NANG_WARDS_BY_DISTRICT[dist] || [];
  };

  const isWardValidForDistrict = (wardName, distName) => {
    const wards = getWardsForDistrict(distName);
    return wards.includes(wardName);
  };

  const formatPhoneDisplay = (phone) => {
    const d = (phone || '').replace(/\s/g, '');
    if (d.length < 10) return phone || '';
    return `${d.slice(0, 4)} ${d.slice(4, 7)} ${d.slice(7)}`;
  };

  const buildGoogleMapsEmbedSrc = (query) => {
    const q = encodeURIComponent(query);
    const key =
      (window.daNangDataConfig && window.daNangDataConfig.googleMapsEmbedApiKey) || '';
    if (key) {
      return `https://www.google.com/maps/embed/v1/place?key=${encodeURIComponent(key)}&q=${q}&zoom=17&language=vi&region=VN`;
    }
    return `https://www.google.com/maps?q=${q}&output=embed&z=17&hl=vi`;
  };

  // ---------------------------------------------------------------------------
  // Helper nội bộ của nguồn (AddressModal.jsx) — port sang để Blazor JS interop
  // gọi được. Logic giữ nguyên văn nguồn.
  // ---------------------------------------------------------------------------

  // wardSuggestionsFor(ward, district)
  const wardSuggestionsFor = (ward, district) => {
    const base =
      district && DA_NANG_WARDS_BY_DISTRICT[district]
        ? DA_NANG_WARDS_BY_DISTRICT[district]
        : ALL_DA_NANG_WARDS;
    const query = (ward || '').trim();
    if (!query) return base.slice(0, 15);
    const normalizedQuery = normalizeText(query);
    return base
      .filter((item) => normalizeText(item).includes(normalizedQuery))
      .slice(0, 15);
  };

  // districtSuggestionsFor(district)
  const districtSuggestionsFor = (district) => {
    const query = (district || '').trim();
    if (!query) return DA_NANG_DISTRICTS.slice(0, 8);
    const normalizedQuery = normalizeText(query);
    return DA_NANG_DISTRICTS
      .filter((item) => normalizeText(item).includes(normalizedQuery))
      .slice(0, 8);
  };

  // isKnownDistrict(district) — tương đương DA_NANG_DISTRICTS.includes(district)
  const isKnownDistrict = (district) => DA_NANG_DISTRICTS.includes(district);

  return {
    DA_NANG_WARDS_BY_DISTRICT,
    DA_NANG_DISTRICTS,
    ALL_DA_NANG_WARDS,
    DEFAULT_PROVINCE,
    normalizeText,
    validatePhone,
    validateEmail,
    getWardsForDistrict,
    isWardValidForDistrict,
    formatPhoneDisplay,
    buildGoogleMapsEmbedSrc,
    wardSuggestionsFor,
    districtSuggestionsFor,
    isKnownDistrict,
  };
})();
