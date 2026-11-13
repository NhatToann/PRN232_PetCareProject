using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Repositories
{
    public sealed class AuthRepository : IAuthRepository
    {
        private readonly ShopPetDatabaseContext _db;

        public AuthRepository(ShopPetDatabaseContext db)
        {
            _db = db;
        }

        public Task<Customer?> GetCustomerByEmailAsync(string email, CancellationToken ct = default)
        {
            var normalizedEmail = NormalizeEmail(email);

            return _db.Customers
                .FirstOrDefaultAsync(x => (x.Email ?? string.Empty).Trim().ToLower() == normalizedEmail, ct);
        }

        public Task<Staff?> GetStaffByEmailAsync(string email, CancellationToken ct = default)
        {
            var normalizedEmail = NormalizeEmail(email);

            return _db.Staff
                .FirstOrDefaultAsync(x => (x.Email ?? string.Empty).Trim().ToLower() == normalizedEmail, ct);
        }

        public Task<Doctor?> GetDoctorByEmailAsync(string email, CancellationToken ct = default)
        {
            var normalizedEmail = NormalizeEmail(email);

            return _db.Doctors
                .FirstOrDefaultAsync(x => (x.Email ?? string.Empty).Trim().ToLower() == normalizedEmail, ct);
        }

        public Task<Admin?> GetAdminByEmailOrUsernameAsync(string input, CancellationToken ct = default)
        {
            var normalizedInput = NormalizeEmail(input);

            return _db.Admins
                .FirstOrDefaultAsync(x =>
                    (x.Email ?? string.Empty).Trim().ToLower() == normalizedInput
                    || x.Username.Trim().ToLower() == normalizedInput,
                    ct);
        }

        public async Task<Customer> CreateCustomerAsync(Customer customer, CancellationToken ct = default)
        {
            await _db.Customers.AddAsync(customer, ct);
            await _db.SaveChangesAsync(ct);
            return customer;
        }

        public async Task UpdateCustomerAsync(Customer customer, CancellationToken ct = default)
        {
            _db.Customers.Update(customer);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<bool> UpdateCustomerAvatarAsync(int customerId, string? avatarUrl, CancellationToken ct = default)
        {
            if (customerId <= 0)
            {
                return false;
            }

            var customer = await _db.Customers
                .AsTracking()
                .FirstOrDefaultAsync(x => x.CustomerId == customerId, ct);
            if (customer is null)
            {
                return false;
            }

            customer.AvatarUrl = avatarUrl;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<string?> GetCustomerAvatarAsync(int customerId, CancellationToken ct = default)
        {
            if (customerId <= 0)
            {
                return null;
            }

            var avatar = await _db.Customers
                .Where(x => x.CustomerId == customerId)
                .Select(x => x.AvatarUrl)
                .FirstOrDefaultAsync(ct);

            return avatar;
        }

        /// <inheritdoc />
        public async Task<bool> UpdateCustomerBirthDateAsync(int customerId, DateTime? birthDate, CancellationToken ct = default)
        {
            if (customerId <= 0)
            {
                return false;
            }

            var customer = await _db.Customers
                .AsTracking()
                .FirstOrDefaultAsync(x => x.CustomerId == customerId, ct);
            if (customer is null)
            {
                return false;
            }

            customer.BirthDate = birthDate;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        /// <inheritdoc />
        public async Task<DateTime?> GetCustomerBirthDateAsync(int customerId, CancellationToken ct = default)
        {
            if (customerId <= 0)
            {
                return null;
            }

            var birthDate = await _db.Customers
                .Where(x => x.CustomerId == customerId)
                .Select(x => x.BirthDate)
                .FirstOrDefaultAsync(ct);

            return birthDate;
        }

        /// <inheritdoc />
        public async Task<bool> UpdateCustomerAddressAsync(int customerId, string? address, CancellationToken ct = default)
        {
            if (customerId <= 0)
            {
                return false;
            }

            var customer = await _db.Customers
                .AsTracking()
                .FirstOrDefaultAsync(x => x.CustomerId == customerId, ct);
            if (customer is null)
            {
                return false;
            }

            customer.AddressCustomer = address;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        /// <inheritdoc />
        public async Task<string?> GetCustomerAddressAsync(int customerId, CancellationToken ct = default)
        {
            if (customerId <= 0)
            {
                return null;
            }

            var address = await _db.Customers
                .Where(x => x.CustomerId == customerId)
                .Select(x => x.AddressCustomer)
                .FirstOrDefaultAsync(ct);

            return address;
        }

        /// <inheritdoc />
        public async Task<bool> UpdateCustomerPhoneAsync(int customerId, string? phone, CancellationToken ct = default)
        {
            if (customerId <= 0)
            {
                return false;
            }

            var customer = await _db.Customers
                .AsTracking()
                .FirstOrDefaultAsync(x => x.CustomerId == customerId, ct);
            if (customer is null)
            {
                return false;
            }

            customer.Phone = phone;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        /// <inheritdoc />
        public async Task<string?> GetCustomerPhoneAsync(int customerId, CancellationToken ct = default)
        {
            if (customerId <= 0)
            {
                return null;
            }

            var phone = await _db.Customers
                .Where(x => x.CustomerId == customerId)
                .Select(x => x.Phone)
                .FirstOrDefaultAsync(ct);

            return phone;
        }

        public async Task<bool> EmailExistsInAnyRoleAsync(string email, CancellationToken ct = default)
        {
            var normalizedEmail = NormalizeEmail(email);

            return await _db.Customers.AnyAsync(x => (x.Email ?? string.Empty).Trim().ToLower() == normalizedEmail, ct)
                || await _db.Staff.AnyAsync(x => (x.Email ?? string.Empty).Trim().ToLower() == normalizedEmail, ct)
                || await _db.Doctors.AnyAsync(x => (x.Email ?? string.Empty).Trim().ToLower() == normalizedEmail, ct)
                || await _db.Admins.AnyAsync(x => (x.Email ?? string.Empty).Trim().ToLower() == normalizedEmail, ct)
                || await _db.Admins.AnyAsync(x => x.Username.Trim().ToLower() == normalizedEmail, ct);
        }

        public async Task<int> UpgradePasswordHashAsync(string email, string newPasswordHash, CancellationToken ct = default)
        {
            var normalizedEmail = NormalizeEmail(email);
            var upgraded = 0;

            var customer = await _db.Customers
                .AsTracking()
                .FirstOrDefaultAsync(
                x => (x.Email ?? string.Empty).Trim().ToLower() == normalizedEmail, ct);
            if (customer is not null)
            {
                customer.Password = newPasswordHash;
                upgraded++;
            }

            var staff = await _db.Staff
                .AsTracking()
                .FirstOrDefaultAsync(
                x => (x.Email ?? string.Empty).Trim().ToLower() == normalizedEmail, ct);
            if (staff is not null)
            {
                staff.Password = newPasswordHash;
                upgraded++;
            }

            var doctor = await _db.Doctors
                .AsTracking()
                .FirstOrDefaultAsync(
                x => (x.Email ?? string.Empty).Trim().ToLower() == normalizedEmail, ct);
            if (doctor is not null)
            {
                doctor.Password = newPasswordHash;
                upgraded++;
            }

            if (upgraded > 0)
            {
                await _db.SaveChangesAsync(ct);
            }

            return upgraded;
        }

        private static string NormalizeEmail(string input)
        {
            return (input ?? string.Empty).Trim().ToLowerInvariant();
        }
    }
}
