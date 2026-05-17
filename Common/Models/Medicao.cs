using Common.Models.Enums;

namespace Common.Models;

/// <summary>
/// Representa uma medição ambiental recolhida por um sensor urbano.
/// Entidade de domínio com validação no construtor (encapsulamento).
/// </summary>
public class Medicao
{
    public string SensorId { get; }
    public string Zona { get; }
    public string TipoDado { get; }
    public double Valor { get; private set; }
    public DateTime Timestamp { get; }
    public FormatoDados Formato { get; }
    public string? PayloadBruto { get; }

    /// <summary>
    /// Cria uma medição validada a partir dos campos essenciais.
    /// </summary>
    public Medicao(
        string sensorId,
        string zona,
        string tipoDado,
        double valor,
        DateTime timestamp,
        FormatoDados formato = FormatoDados.None,
        string? payloadBruto = null)
    {
        if (string.IsNullOrWhiteSpace(sensorId))
            throw new ArgumentException("O identificador do sensor é obrigatório.", nameof(sensorId));
        if (string.IsNullOrWhiteSpace(zona))
            throw new ArgumentException("A zona é obrigatória.", nameof(zona));
        if (string.IsNullOrWhiteSpace(tipoDado))
            throw new ArgumentException("O tipo de dado é obrigatório.", nameof(tipoDado));

        SensorId = sensorId.Trim();
        Zona = zona.Trim();
        TipoDado = tipoDado.Trim();
        Valor = valor;
        Timestamp = timestamp;
        Formato = formato;
        PayloadBruto = payloadBruto;
    }

    /// <summary>
    /// Atualiza o valor após pré-processamento (ex.: conversão de escala).
    /// </summary>
    public void AtualizarValor(double novoValor) => Valor = novoValor;

    /// <summary>
    /// Converte a medição para o formato de linha usado na comunicação TCP com o servidor.
    /// </summary>
    public string ParaMensagemTcp()
    {
        string ts = Timestamp.ToString("yyyy-MM-ddTHH:mm:ss");
        return $"DATA|{SensorId}|{Zona}|{TipoDado}|{Valor.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{ts}";
    }
}
