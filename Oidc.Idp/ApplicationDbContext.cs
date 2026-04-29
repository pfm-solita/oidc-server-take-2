using Microsoft.EntityFrameworkCore;

namespace Oidc.Idp;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
}
