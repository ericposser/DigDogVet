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

    // Permissões ficam em cache por 5 minutos por usuário.
    // Ao editar uma role, o cache é invalidado manualmente via InvalidarCacheAsync.
    private static readonly TimeSpan TtlCachePermissao = TimeSpan.FromMinutes(5);
    private const string PrefixoCache = "permissao:";

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
        // Admin não usa cache — é verificado diretamente no cookie/claims
        var usuario = await _gerenciadorUsuario.FindByIdAsync(idUsuario);
        if (usuario == null) return false;

        if (await _gerenciadorUsuario.IsInRoleAsync(usuario, "Admin"))
            return true;

        // Carrega (ou busca do cache) o conjunto de permissões do usuário
        var permissoes = await ObterPermissoesUsuarioAsync(idUsuario, usuario);
        return permissoes.Contains(permissao);
    }

    /// <summary>
    /// Retorna o conjunto de permissões do usuário.
    /// Na primeira chamada consulta o banco; nas seguintes usa o cache por 5 min.
    /// </summary>
    private async Task<HashSet<Permissao>> ObterPermissoesUsuarioAsync(
        string idUsuario, IdentityUser usuario)
    {
        var chaveCache = $"{PrefixoCache}{idUsuario}";

        if (_cache.TryGetValue(chaveCache, out HashSet<Permissao>? permissoesCache)
            && permissoesCache != null)
            return permissoesCache;

        // Cache miss — consulta o banco
        var roles = await _gerenciadorUsuario.GetRolesAsync(usuario);
        if (!roles.Any())
        {
            var vazio = new HashSet<Permissao>();
            _cache.Set(chaveCache, vazio, TtlCachePermissao);
            return vazio;
        }

        var roleIds = await _gerenciadorRole.Roles
            .Where(r => roles.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync();

        var permissoesBanco = await _contexto.RolePermissao
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.Permissao)
            .ToListAsync();

        var conjunto = permissoesBanco.ToHashSet();
        _cache.Set(chaveCache, conjunto, TtlCachePermissao);
        return conjunto;
    }

    /// <summary>
    /// Invalida o cache de permissões de todos os usuários que têm a role alterada.
    /// Deve ser chamado após salvar ou excluir uma role.
    /// </summary>
    public async Task InvalidarCacheRoleAsync(string roleId)
    {
        // Busca todos os usuários com essa role para invalidar o cache deles
        var nomesRole = await _gerenciadorRole.Roles
            .Where(r => r.Id == roleId)
            .Select(r => r.Name!)
            .ToListAsync();

        foreach (var nomeRole in nomesRole)
        {
            var usuariosNaRole = await _gerenciadorUsuario.GetUsersInRoleAsync(nomeRole);
            foreach (var usuario in usuariosNaRole)
                _cache.Remove($"{PrefixoCache}{usuario.Id}");
        }
    }

    // ── Operações de Role ─────────────────────────────────────────────────

    public async Task<List<IdentityRole>> ListarRolesCustomizadasAsync()
    {
        return await _gerenciadorRole.Roles
            .Where(r => r.Name != "Admin")
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<List<Permissao>> ObterPermissoesRoleAsync(string roleId)
    {
        return await _contexto.RolePermissao
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permissao)
            .ToListAsync();
    }

    public async Task SalvarPermissoesRoleAsync(string roleId, List<Permissao> permissoes)
    {
        var existentes = _contexto.RolePermissao.Where(rp => rp.RoleId == roleId);
        _contexto.RolePermissao.RemoveRange(existentes);

        foreach (var permissao in permissoes.Distinct())
        {
            _contexto.RolePermissao.Add(new RolePermissao
            {
                RoleId    = roleId,
                Permissao = permissao
            });
        }

        await _contexto.SaveChangesAsync();

        // Invalida o cache dos usuários afetados pela mudança de role
        await InvalidarCacheRoleAsync(roleId);
    }

    public async Task RemoverTodasPermissoesRoleAsync(string roleId)
    {
        var permissoes = _contexto.RolePermissao.Where(rp => rp.RoleId == roleId);
        _contexto.RolePermissao.RemoveRange(permissoes);
        await _contexto.SaveChangesAsync();
        await InvalidarCacheRoleAsync(roleId);
    }

    // ── Helpers para o formulário ─────────────────────────────────────────

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
