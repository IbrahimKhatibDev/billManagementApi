using BillsMinimalApi.Dtos;
using BillsMinimalApi.Models;

namespace BillsMinimalApi.Mappers;

public static class BillMapper
{
    public static BillDto ToDto(Bill entity) => new()
    {
        Id = entity.Id,
        PayeeName = entity.PayeeName,
        DueDate = entity.DueDate,
        PaymentDue = entity.PaymentDue,
        Paid = entity.Paid,
        Version = entity.Version
    };

    public static Bill ToEntity(BillDto dto) => new()
    {
        Id = dto.Id,
        PayeeName = dto.PayeeName,
        DueDate = dto.DueDate,
        PaymentDue = dto.PaymentDue,
        Paid = dto.Paid,
        Version = dto.Version
    };
}