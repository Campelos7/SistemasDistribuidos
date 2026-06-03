using System.Collections.Concurrent;
using Common.Interfaces;
using Common.Models;

namespace Common.Services;

/// <summary>
/// Implementa o <b>Padrão Dispatcher-Worker</b> para persistência de medições no SQLite.
/// </summary>
/// <remarks>
/// <para>
/// O SQLite é uma base de dados local baseada num único ficheiro. Quando o Servidor recebe
/// ligações TCP de múltiplos Gateways em simultâneo, várias threads de rede (dispatchers)
/// não podem escrever directamente na BD — originam excepções de concorrência
/// (<c>database is locked</c>).
/// </para>
/// <para>
/// Este componente <b>isola o contexto de rede do contexto de I/O de disco</b>:
/// </para>
/// <list type="bullet">
///   <item><description><b>Dispatchers</b> — threads TCP (<see cref="Enfileirar"/>) apenas colocam
///   medições numa fila em memória e devolvem controlo imediatamente (rede não bloqueante).</description></item>
///   <item><description><b>Fila</b> — <see cref="ConcurrentQueue{T}"/> thread-safe entre produtores e consumidor.</description></item>
///   <item><description><b>Worker</b> — uma única thread em background consome a fila e invoca
///   <see cref="IMedicaoRepository.Guardar"/>, garantindo <b>thread-safety absoluta</b> nas escritas.</description></item>
/// </list>
/// <para>
/// Instanciado em <c>TrabalhoPratico/Program.cs</c> e injectado no <see cref="ServidorService"/>.
/// </para>
/// </remarks>
public sealed class MedicaoPersistenceWorker : IDisposable
{
    private readonly IMedicaoRepository _repository;

    /// <summary>
    /// Fila em memória partilhada entre todas as threads dispatcher e a thread worker.
    /// <see cref="ConcurrentQueue{T}"/> garante enfileiramento/desencadeamento thread-safe
    /// sem locks explícitos nas threads de rede.
    /// </summary>
    private readonly ConcurrentQueue<Medicao> _fila = new();

    /// <summary>Thread única responsável por todas as escritas de medições no SQLite (worker).</summary>
    private readonly Thread _worker;

    /// <summary>Sinaliza ao worker que deve terminar após esvaziar a fila pendente.</summary>
    private volatile bool _executando = true;

    /// <summary>
    /// Cria o worker e arranca a thread de I/O de disco em background.
    /// </summary>
    /// <param name="repository">Repositório SQLite; apenas a thread worker invoca <see cref="IMedicaoRepository.Guardar"/>.</param>
    public MedicaoPersistenceWorker(IMedicaoRepository repository)
    {
        _repository = repository;
        _worker = new Thread(ExecutarLoop)
        {
            IsBackground = true,
            Name = "MedicaoPersistenceWorker"
        };
        _worker.Start();
        Console.WriteLine("[WORKER] Thread de persistência SQLite iniciada.");
    }

    /// <summary>
    /// Ponto de entrada dos <b>dispatchers</b> (threads TCP): enfileira a medição sem aceder ao disco.
    /// </summary>
    /// <param name="medicao">Medição validada pelo <see cref="ServidorService"/>.</param>
    /// <remarks>
    /// Após chamar este método, o dispatcher pode responder <c>ACK</c> ao gateway —
    /// a persistência efectiva ocorre de forma assíncrona na thread worker.
    /// </remarks>
    public void Enfileirar(Medicao medicao) => _fila.Enqueue(medicao);

    /// <summary>
    /// Loop da thread <b>worker</b>: consome a fila e persiste cada medição no SQLite.
    /// </summary>
    /// <remarks>
    /// Serializa todas as escritas numa única thread, eliminando condições de corrida
    /// e conflitos de lock da BD. Continua a processar itens pendentes mesmo após
    /// <see cref="Dispose"/> sinalizar paragem.
    /// </remarks>
    private void ExecutarLoop()
    {
        while (_executando || !_fila.IsEmpty)
        {
            if (_fila.TryDequeue(out Medicao? medicao))
            {
                try
                {
                    _repository.Guardar(medicao);
                    Console.WriteLine($"[WORKER] Medição guardada: {medicao.SensorId} | {medicao.TipoDado} | {medicao.Valor}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WORKER] Erro ao guardar medição: {ex.Message}");
                }
            }
            else if (_executando)
            {
                Thread.Sleep(10);
            }
        }
    }

    /// <summary>
    /// Solicita paragem graciosa do worker e aguarda conclusão (até 5 s).
    /// </summary>
    /// <remarks>
    /// A thread worker esvazia a fila antes de terminar, minimizando perda de medições
    /// enfileiradas no encerramento do processo Servidor.
    /// </remarks>
    public void Dispose()
    {
        _executando = false;
        _worker.Join(TimeSpan.FromSeconds(5));
    }
}
