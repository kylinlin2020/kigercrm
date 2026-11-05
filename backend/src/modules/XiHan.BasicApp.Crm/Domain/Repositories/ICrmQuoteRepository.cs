using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

/// <summary>Tenant-scoped repository for quotation headers.</summary>
public interface ICrmQuoteRepository : ISaasRepository<SysCrmQuote> { }
