using DigDog.Data;
using DigDog.Filters;
using DigDog.Models;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DigDog.Controllers;

[Authorize]
public class ProdutoController : UtilController
{
    private readonly Contexto _contexto;
    private readonly LogService _logService;

    public ProdutoController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        LogService logService,
        IMemoryCache cache)
        : base(gerenciadorUsuario, provedorProtecao, contexto, cache)
    {
        _contexto   = contexto;
        _logService = logService;
    }

    [RequerPermissao(Permissao.ProdutosVisualizar)]
    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        var produtos = await _contexto.Produto
            .AsNoTracking()
            .Where(p => p.IdEmpresa == idEmpresa)
            .OrderBy(p => p.Nome)
            .ToListAsync();
        return View(produtos);
    }

    [RequerPermissao(Permissao.ProdutosCriar)]
    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.ProdutosCriar)]
    public async Task<IActionResult> Create([Bind("Nome,Descricao,Preco,Estoque")] Produto produto)
    {
        produto.IdEmpresa = await ObterIdEmpresaAsync();
        RemoverValidacaoUsuario();
        if (ModelState.IsValid)
        {
            _contexto.Add(produto);
            await _contexto.SaveChangesAsync();
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Criou", "Produtos",
                $"Cadastrou o produto {produto.Nome}",
                produto.IdEmpresa);
            DefinirToast("Produto cadastrado com sucesso!", "success");
            return RedirectToAction(nameof(Index));
        }
        return View(produto);
    }

    [RequerPermissao(Permissao.ProdutosEditar)]
    public async Task<IActionResult> Edit(string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var produto = await _contexto.Produto
            .AsNoTracking()
            .Where(p => p.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(p => p.Id == idReal);
        if (produto == null) return NotFound();
        ViewBag.IdCriptografado = CriptografarId(produto.Id);
        return View(produto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.ProdutosEditar)]
    public async Task<IActionResult> Edit(string id, [Bind("Nome,Descricao,Preco,Estoque")] Produto produto)
    {
        var idRota = RouteData.Values["id"]?.ToString();
        var idReal = DescriptografarId(idRota ?? id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        RemoverValidacaoUsuario();
        var produtoExistente = await _contexto.Produto
            .Where(p => p.IdEmpresa == idEmpresa && p.Id == idReal)
            .FirstOrDefaultAsync();
        if (produtoExistente == null) return NotFound();
        if (ModelState.IsValid)
        {
            produtoExistente.Nome      = produto.Nome;
            produtoExistente.Descricao = produto.Descricao;
            produtoExistente.Preco     = produto.Preco;
            produtoExistente.Estoque   = produto.Estoque;
            try { await _contexto.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                if (!await ProdutoExisteAsync(idReal.Value, idEmpresa)) return NotFound();
                throw;
            }
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Editou", "Produtos",
                $"Atualizou o produto {produtoExistente.Nome}",
                idEmpresa);
            DefinirToast("Produto atualizado com sucesso!", "warning");
            return RedirectToAction(nameof(Index));
        }
        ViewBag.IdCriptografado = CriptografarId(idReal.Value);
        return View(produto);
    }

    [RequerPermissao(Permissao.ProdutosExcluir)]
    public async Task<IActionResult> Delete(string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var produto = await _contexto.Produto
            .AsNoTracking()
            .Where(p => p.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(p => p.Id == idReal);
        if (produto == null) return NotFound();
        ViewBag.IdCriptografado = CriptografarId(produto.Id);
        return View(produto);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.ProdutosExcluir)]
    public async Task<IActionResult> DeleteConfirmed([FromRoute] string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var produto = await _contexto.Produto
            .Where(p => p.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(p => p.Id == idReal);
        if (produto != null)
        {
            var nome = produto.Nome;
            _contexto.Produto.Remove(produto);
            await _contexto.SaveChangesAsync();
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Excluiu", "Produtos",
                $"Removeu o produto {nome}",
                idEmpresa);
        }
        DefinirToast("Produto excluído com sucesso!", "danger");
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> ProdutoExisteAsync(int id, int idEmpresa) =>
        await _contexto.Produto.AnyAsync(p => p.Id == id && p.IdEmpresa == idEmpresa);
}