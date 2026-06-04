using DigDog.Data;
using DigDog.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DigDog.Services;

public class PermissaoService
{
    private readonly Contexto _contexto;
    private readonly UserManager<IdentityUser> _gerenciadorUsuario;
    private readonly RoleManager<IdentityRole> _gerenciadorRole;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan TtlCachePermissao = TimeSpan.FromMinutes(5);
    private const string PrefixoCachePermissoes = "permissao:";
    private const string PrefixoCacheEhAdmin    = "ehAdmin:";

    public PermissaoService(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        RoleManager<IdentityRole> gerenciadorRole,
        IMemoryCache cache)
    {
        _contexto           = contexto;
        _gerenciadorUsuario = gerenciadorUsuario;
        _gerenciadorRole    = gerenciadorRole;
        _cache              = cache;
    }

    // ── Verificação de acesso ─────────────────────────────────────────────

    public async Task<bool> UsuarioTemPermissaoAsync(string idUsuario, Permissao permissao)
    {
        // 1. Verifica admin (cacheado também)
        if (await EhAdminAsync(idUsuario))
            return true;

        // 2. Verifica conjunto de permissões cacheado
        var permissoes = await ObterTodasPermissoesUsuarioAsync(idUsuario);
        return permissoes.Contains(permissao);
    }

    /// <summary>
    /// Verifica se o usuário tem a role "Admin". Cacheado por 5 minutos.
    /// </summary>
    public async Task<bool> EhAdminAsync(string idUsuario)
    {
        var chaveCache = $"{PrefixoCacheEhAdmin}{idUsuario}";

        if (_cache.TryGetValue<bool>(chaveCache, out var ehAdminCache))
            return ehAdminCache;

        // Query direta nas tabelas do Identity, sem passar pelo UserManager
        var ehAdmin = await (
            from ur in _contexto.UserRoles.AsNoTracking()
            join r  in _contexto.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ur.UserId == idUsuario && r.Name == "Admin"
            select 1
        ).AnyAsync();

        _cache.Set(chaveCache, ehAdmin, TtlCachePermissao);
        return ehAdmin;
    }

    /// <summary>
    /// Retorna o conjunto completo de permissões do usuário em UMA query.
    /// Usado pelo _Layout.cshtml pra evitar 30 chamadas separadas.
    /// </summary>
    public async Task<HashSet<Permissao>> ObterTodasPermissoesUsuarioAsync(string idUsuario)
    {
        var chaveCache = $"{PrefixoCachePermissoes}{idUsuario}";

        if (_cache.TryGetValue(chaveCache, out HashSet<Permissao>? permissoesCache)
            && permissoesCache != null)
            return permissoesCache;

        // Uma única query: JOIN UserRoles × RolePermissao
        // Substitui as 3 queries originais (GetRoles + Roles + RolePermissao)
        var permissoes = await (
            from ur in _contexto.UserRoles.AsNoTracking()
            join rp in _contexto.RolePermissao.AsNoTracking() on ur.RoleId equals rp.RoleId
            where ur.UserId == idUsuario
            select rp.Permissao
        ).Distinct().ToListAsync();

        var conjunto = permissoes.ToHashSet();
        _cache.Set(chaveCache, conjunto, TtlCachePermissao);
        return conjunto;
    }

    /// <summary>
    /// Invalida o cache do usuário. Chamar após alterar role ou permissões individuais.
    /// </summary>
    public void InvalidarCacheUsuario(string idUsuario)
    {
        _cache.Remove($"{PrefixoCachePermissoes}{idUsuario}");
        _cache.Remove($"{PrefixoCacheEhAdmin}{idUsuario}");
    }

    /// <summary>
    /// Invalida o cache de todos os usuários que têm a role alterada.
    /// Chamar após salvar ou excluir uma role.
    /// </summary>
    public async Task InvalidarCacheRoleAsync(string roleId)
    {
        var nomeRole = await _contexto.Roles
            .AsNoTracking()
            .Where(r => r.Id == roleId)
            .Select(r => r.Name)
            .FirstOrDefaultAsync();

        if (nomeRole == null) return;

        // IDs dos usuários com essa role — uma query direta
        var idsUsuarios = await (
            from ur in _contexto.UserRoles.AsNoTracking()
            join r  in _contexto.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where r.Name == nomeRole
            select ur.UserId
        ).ToListAsync();

        foreach (var id in idsUsuarios)
            InvalidarCacheUsuario(id);
    }

    // ── Operações de Role ─────────────────────────────────────────────────

    public async Task<List<IdentityRole>> ListarRolesCustomizadasAsync()
    {
        return await _gerenciadorRole.Roles
            .AsNoTracking()
            .Where(r => r.Name != "Admin")
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<List<Permissao>> ObterPermissoesRoleAsync(string roleId)
    {
        return await _contexto.RolePermissao
            .AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permissao)
            .ToListAsync();
    }

    public async Task SalvarPermissoesRoleAsync(string roleId, List<Permissao> permissoes)
    {
        // DELETE em massa sem carregar entidades
        await _contexto.RolePermissao
            .Where(rp => rp.RoleId == roleId)
            .ExecuteDeleteAsync();

        foreach (var permissao in permissoes.Distinct())
        {
            _contexto.RolePermissao.Add(new RolePermissao
            {
                RoleId    = roleId,
                Permissao = permissao
            });
        }

        await _contexto.SaveChangesAsync();
        await InvalidarCacheRoleAsync(roleId);
    }

    public async Task RemoverTodasPermissoesRoleAsync(string roleId)
    {
        await _contexto.RolePermissao
            .Where(rp => rp.RoleId == roleId)
            .ExecuteDeleteAsync();
        await InvalidarCacheRoleAsync(roleId);
    }

    // ── Helpers para o formulário (sem mudanças) ──────────────────────────

    public Dictionary<string, List<PermissaoOpcao>> MontarGruposPermissao(
        List<Permissao> selecionadas)
    {
        var grupos = new Dictionary<string, List<PermissaoOpcao>>();

        var mapeamento = new Dictionary<string, string>
        {
            { "PainelFaturamento", "Painel de Faturamento" },
            { "Tutores",           "Tutores"               },
            { "Pets",              "Pets"                  },
            { "Consultas",         "Consultas"             },
            { "Vacinas",           "Vacinas"               },
            { "Vacinacao",         "Carteira de Vacinação" },
            { "BanhoTosa",         "Banho & Tosa"          },
            { "StatusDia",         "Status do Dia"         },
            { "Produtos",          "Produtos"              },
            { "Vendas",            "Vendas"                },
        };

        foreach (var entrada in mapeamento)
            grupos[entrada.Value] = new List<PermissaoOpcao>();

        foreach (Permissao permissao in Enum.GetValues(typeof(Permissao)))
        {
            var nome        = permissao.ToString();
            var moduloChave = ExtrairModulo(nome);
            var acao        = ExtrairAcao(nome);

            var labelModulo = mapeamento.TryGetValue(moduloChave, out var label)
                ? label
                : moduloChave;

            if (!grupos.ContainsKey(labelModulo))
                grupos[labelModulo] = new List<PermissaoOpcao>();

            grupos[labelModulo].Add(new PermissaoOpcao
            {
                Valor       = permissao,
                Label       = acao,
                Selecionada = selecionadas.Contains(permissao)
            });
        }

        foreach (var chave in grupos.Keys.Where(k => grupos[k].Count == 0).ToList())
            grupos.Remove(chave);

        return grupos;
    }

    private static string ExtrairModulo(string nomePermissao)
    {
        string[] acoes = { "Visualizar", "Criar", "Editar", "Excluir" };
        foreach (var acao in acoes)
            if (nomePermissao.EndsWith(acao)) return nomePermissao[..^acao.Length];
        return nomePermissao;
    }

    private static string ExtrairAcao(string nomePermissao)
    {
        string[] acoes = { "Visualizar", "Criar", "Editar", "Excluir" };
        foreach (var acao in acoes)
            if (nomePermissao.EndsWith(acao)) return acao;
        return nomePermissao;
    }
}