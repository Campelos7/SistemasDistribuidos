using Common.Interfaces;
using Common.Models.Enums;

namespace Gateway.Services;

/// <summary>
/// Monitoriza sensores ativos e marca como desativados após timeout de heartbeat.
/// </summary>
public class HeartbeatMonitor
{
    private readonly ISensorRegistoRepository _repository;
    private readonly int _timeoutSegundos;
    private CancellationTokenSource? _cts;

    public HeartbeatMonitor(ISensorRegistoRepository repository, int timeoutSegundos)
    {
        _repository = repository;
        _timeoutSegundos = timeoutSegundos;
    }

    /// <summary>
    /// Inicia verificação periódica em background.
    /// </summary>
    public void Iniciar()
    {
        _cts = new CancellationTokenSource();
        Task.Run(() => LoopAsync(_cts.Token));
        Console.WriteLine("[GATEWAY] Monitorização de heartbeats ativa.");
    }

    public void Parar() => _cts?.Cancel();

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            VerificarTimeouts();
        }
    }

    private void VerificarTimeouts()
    {
        var sensores = _repository.CarregarTodos();
        foreach (var par in sensores)
        {
            var s = par.Value;
            if (s.Estado != EstadoSensor.Ativo || !s.UltimaSincronizacao.HasValue)
                continue;

            double segundos = (DateTime.Now - s.UltimaSincronizacao.Value).TotalSeconds;
            if (segundos > _timeoutSegundos)
            {
                Console.WriteLine($"[GATEWAY] Sensor {s.SensorId} sem heartbeat há {(int)segundos}s — a desativar.");
                _repository.MarcarDesativado(s.SensorId);
            }
        }
    }
}
