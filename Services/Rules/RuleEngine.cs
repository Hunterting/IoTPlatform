using IoTPlatform.Models;
using System.Text.Json;

namespace IoTPlatform.Services.Rules;

/// <summary>
/// 规则引擎
/// </summary>
public class RuleEngine
{
    /// <summary>
    /// 执行规则
    /// </summary>
    public bool ExecuteRule(DataRule rule, DeviceDataRecord data)
    {
        if (!rule.IsActive)
        {
            return false;
        }

        // 解析传感器数据
        var sensorData = ParseSensorData(data.SensorData ?? "{}");
        if (sensorData.Count == 0)
        {
            return false;
        }

        // 检查数据类型是否匹配
        if (!string.IsNullOrEmpty(rule.DataType))
        {
            if (!sensorData.ContainsKey(rule.DataType))
            {
                return false;
            }
        }

        var value = sensorData[rule.DataType];

        // 执行规则
        switch (rule.RuleType)
        {
            case "alert":
                return ExecuteAlertRule(rule, value);
            case "transform":
                return ExecuteTransformRule(rule, value);
            case "validation":
                return ExecuteValidationRule(rule, value);
            default:
                return false;
        }
    }

    /// <summary>
    /// 执行告警规则
    /// </summary>
    private bool ExecuteAlertRule(DataRule rule, object value)
    {
        if (value == null || !double.TryParse(value.ToString(), out double numericValue))
        {
            return false;
        }

        // 阈值判断
        bool trigger = false;
        if (rule.MinValue.HasValue && numericValue < rule.MinValue.Value)
        {
            trigger = true;
        }
        if (rule.MaxValue.HasValue && numericValue > rule.MaxValue.Value)
        {
            trigger = true;
        }

        return trigger;
    }

    /// <summary>
    /// 执行转换规则
    /// </summary>
    private bool ExecuteTransformRule(DataRule rule, object value)
    {
        // TODO: 实现数据转换逻辑
        return false;
    }

    /// <summary>
    /// 执行验证规则
    /// </summary>
    private bool ExecuteValidationRule(DataRule rule, object value)
    {
        if (value == null || !double.TryParse(value.ToString(), out double numericValue))
        {
            return false;
        }

        // 验证数值是否在范围内
        bool isValid = true;
        if (rule.MinValue.HasValue && numericValue < rule.MinValue.Value)
        {
            isValid = false;
        }
        if (rule.MaxValue.HasValue && numericValue > rule.MaxValue.Value)
        {
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// 解析传感器数据JSON
    /// </summary>
    public Dictionary<string, object> ParseSensorData(string sensorDataJson)
    {
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(sensorDataJson);
            return data ?? new Dictionary<string, object>();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// 解析规则表达式
    /// </summary>
    public object? ParseRuleExpression(string expression)
    {
        // TODO: 实现复杂规则表达式解析
        return null;
    }
}
