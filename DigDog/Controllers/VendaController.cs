using DigDog.Data;
using DigDog.Filters;
using DigDog.Models;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DigDog.Controllers;

[Authorize]
public class VendaController : UtilController
{
    private readonly Contexto _contexto;
    private readonly LogService _logService;

    public VendaController(
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

    [RequerPermissao(Permissao.VendasVisualizar)]
    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        var vendas    = await _contexto.Venda
            .AsNoTracking()
            .Include(v => v.Produto)
            .Where(v => v.IdEmpresa == idEmpresa)
            .OrderByDescending(v => v.DataVenda)
            .ToListAsync();
        return View(vendas);
    }

    [RequerPermissao(Permissao.VendasCriar)]
    public async Task<IActionResult> Create()
    {
        await CarregarProdutos(await ObterIdEmpresaAsync());
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.VendasCriar)]
    public async Task<IActionResult> Create([Bind("Quantidade,Observacoes")] Venda venda, string idProduto)
    {
        var idEmpresa   = await ObterIdEmpresaAsync();
        venda.IdEmpresa = idEmpresa;
        RemoverValidacaoUsuario();
        ModelState.Remove(nameof(venda.IdProduto));

        var idProdutoReal = DescriptografarId(idProduto);
        if (idProdutoReal == null)
            ModelState.AddModelError(nameof(venda.IdProduto), "Produto inválido.");
        else
            venda.IdProduto = idProdutoReal.Value;

        ValidarTamanhoTexto(nameof(venda.Observacoes), venda.Observacoes, 500);

        if (venda.IdProduto == 0)
            ModelState.AddModelError(nameof(venda.IdProduto), "Selecione o produto.");

        // Produto precisa de tracking — vamos modificar o Estoque
        var produto = await _contexto.Produto
            .Where(p => p.IdEmpresa == idEmpresa && p.Id == venda.IdProduto)
            .FirstOrDefaultAsync();

        if (produto == null)
            ModelState.AddModelError(nameof(venda.IdProduto), "Produto não encontrado.");
        else if (venda.Quantidade <= 0)
            ModelState.AddModelError(nameof(venda.Quantidade), "A quantidade deve ser maior que zero.");
        else if (venda.Quantidade > produto.Estoque)
            ModelState.AddModelError(nameof(venda.Quantidade),
                $"Estoque insuficiente. Disponível: {produto.Estoque} unidade(s).");

        if (ModelState.IsValid)
        {
            venda.PrecoUnitario = produto!.Preco;
            venda.Total         = venda.Quantidade * venda.PrecoUnitario;
            venda.DataVenda     = DateTime.Now;
            produto.Estoque    -= venda.Quantidade;
            _contexto.Add(venda);
            await _contexto.SaveChangesAsync();
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Criou", "Vendas",
                $"Registrou venda de {venda.Quantidade}x {produto.Nome} — Total: R$ {venda.Total:N2}",
                idEmpresa);
            DefinirToast("Venda registrada com sucesso!", "success");
            return RedirectToAction(nameof(Index));
        }
        await CarregarProdutos(idEmpresa, venda.IdProduto);
        return View(venda);
    }

    [RequerPermissao(Permissao.VendasExcluir)]
    public async Task<IActionResult> Delete(string id)
    {
        var idReal    = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var venda     = await _contexto.Venda
            .AsNoTracking()
            .Include(v => v.Produto)
            .Where(v => v.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(v => v.Id == idReal);
        if (venda == null) return NotFound();
        ViewBag.IdCriptografado = CriptografarId(venda.Id);
        return View(venda);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.VendasExcluir)]
    public async Task<IActionResult> DeleteConfirmed([FromRoute] string id)
    {
        var idRota = RouteData.Values["id"]?.ToString();
        var idReal = DescriptografarId(idRota ?? id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();

        // Precisa de tracking — vamos restaurar o estoque
        var venda = await _contexto.Venda
            .Include(v => v.Produto)
            .Where(v => v.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(v => v.Id == idReal);
        if (venda == null)
        {
            DefinirToast("Venda não encontrada.", "danger");
            return RedirectToAction(nameof(Index));
        }
        var descricao = $"Cancelou venda de {venda.Quantidade}x {venda.Produto?.Nome} — Total: R$ {venda.Total:N2}";
        if (venda.Produto != null)
            venda.Produto.Estoque += venda.Quantidade;
        _contexto.Venda.Remove(venda);
        await _contexto.SaveChangesAsync();
        await _logService.RegistrarAsync(
            await ObterNomeFuncionarioAsync(),
            "Excluiu", "Vendas",
            descricao,
            idEmpresa);
        DefinirToast("Venda cancelada e estoque restaurado com sucesso!", "danger");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ObterPrecoProduto(string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();

        var idEmpresa = await ObterIdEmpresaAsync();
        var produto   = await _contexto.Produto
            .AsNoTracking()
            .Where(p => p.IdEmpresa == idEmpresa && p.Id == idReal)
            .Select(p => new { p.Preco, p.Estoque })
            .FirstOrDefaultAsync();

        if (produto == null) return NotFound();
        return Json(produto);
    }

    private async Task CarregarProdutos(int idEmpresa, int? idSelecionado = null)
    {
        // Projeção no banco: só Id e Nome — não traz Descricao, Preco, etc
        var produtos = await _contexto.Produto
            .AsNoTracking()
            .Where(p => p.IdEmpresa == idEmpresa && p.Estoque > 0)
            .OrderBy(p => p.Nome)
            .Select(p => new { p.Id, p.Nome })
            .ToListAsync();

        var itens = produtos.Select(p => new SelectListItem
        {
            Value    = CriptografarId(p.Id),
            Text     = p.Nome,
            Selected = p.Id == idSelecionado
        }).ToList();
        ViewData["IdProduto"] = itens;
    }
}