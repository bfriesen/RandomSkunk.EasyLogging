﻿using System.Diagnostics;
using Microsoft.Extensions.Logging;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace RandomSkunk.Logging;

#if NET7_0_OR_GREATER
using static ArgumentNullException;
#else
using static ThrowHelper;
#endif

/// <summary>
/// An implementation of the <see cref="ILogger"/> interface. It is designed to fulfill the following requirements:
/// <list type="bullet">
///     <item>
///         It should make setup and verification of a mock <see cref="ILogger"/> easy, regardless of the mocking library.
///     </item>
///     <item>
///         As a base class for a custom <see cref="ILogger"/>, it should be both easy to implement and easy to understand
///         <em>how</em> to implement it.
///     </item>
///     <item>
///         It should correctly implement logging scopes and make this information easily available to a test or custom logger
///         implementation.
///     </item>
///     <item>
///         It should have minimal impact on performance.
///     </item>
/// </list>
/// </summary>
public abstract class EasyLogger : ILogger
{
    private readonly AsyncLocal<Scope?> _currentScope = new();

    private LogLevel _minimumLogLevel = LogLevel.Information;
    private bool _includeScopes = true;
    private bool _includeScopesLocked;

    /// <summary>
    /// Gets or sets the minimum log level that the logger should write.
    /// </summary>
    /// <remarks>
    /// Default value is <see cref="LogLevel.Information"/>.
    /// </remarks>
    public LogLevel MinimumLogLevel
    {
        get => _minimumLogLevel;
        set
        {
            if (value < LogLevel.Trace || value > LogLevel.None)
                throw new ArgumentOutOfRangeException(nameof(value));

            _minimumLogLevel = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether scopes should be included in log entries. Default value is
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// This property can only be changed before the logger is used. After the logger is used, attempting to change this property
    /// does nothing.
    /// </remarks>
    public bool IncludeScopes
    {
        get => _includeScopes;
        set
        {
            if (!_includeScopesLocked)
                _includeScopes = value;
        }
    }

    /// <summary>
    /// Gets a collection that represents the logger's current scope stack.
    /// </summary>
    public IEnumerable<object> CurrentScope
    {
        get
        {
            if (!LockAndGetIncludeScopes())
                yield break;

            for (var scope = _currentScope.Value; scope is not null; scope = scope.ParentScope)
                yield return scope.State;
        }
    }

    /// <summary>
    /// When overridden in a derived class, writes the specified log entry.
    /// </summary>
    /// <remarks>
    /// This method is called by <see cref="EasyLogger"/>'s <see cref="Log"/> method after verifying that <see cref="IsEnabled"/>
    /// is <see langword="true"/> for the given log level.
    /// </remarks>
    /// <param name="logEntry">The log entry to write.</param>
    public abstract void Write(LogEntry logEntry);

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!((ILogger)this).IsEnabled(logLevel))
            return;

        var getMessage = () => formatter(state, exception);
        var currentScope = LockAndGetIncludeScopes() ? _currentScope.Value : null;
        var attributes = new LogAttributes(state, currentScope);
        var logEntry = new LogEntry(logLevel, eventId, getMessage, attributes, exception);
        Write(logEntry);
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) =>
        logLevel >= _minimumLogLevel && logLevel < LogLevel.None;

    /// <summary>
    /// Begins a logical operation scope by pushing the specified state onto the logger's scope stack.
    /// </summary>
    /// <typeparam name="TState">The type of the state to begin scope for.</typeparam>
    /// <param name="state"></param>
    /// <returns>An object that, when disposed, pops the state off the logger's scope stack.</returns>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        ThrowIfNull(state);

        if (LockAndGetIncludeScopes())
            return _currentScope.Value = new Scope(state, this);

        return null;
    }

    private void EndScope(Scope scope)
    {
        Debug.Assert(_includeScopes);
        Debug.Assert(_includeScopesLocked);

        // Gracefully handle the possibility of scopes disposing out of order.
        while (_currentScope.Value is not null)
        {
            try
            {
                if (ReferenceEquals(_currentScope.Value, scope))
                    break;
            }
            finally
            {
                _currentScope.Value = _currentScope.Value.ParentScope;
            }
        }
    }

    private bool LockAndGetIncludeScopes()
    {
        _includeScopesLocked = true;
        return _includeScopes;
    }

    private sealed class Scope(object state, EasyLogger logger)
        : ILoggerScope, IDisposable
    {
        public object State { get; } = state;

        public Scope? ParentScope { get; } = logger._currentScope.Value;

        ILoggerScope? ILoggerScope.ParentScope => ParentScope;

        public void Dispose() => logger.EndScope(this);
    }
}

/// <summary>
/// An implementation of the <see cref="ILogger{TCategoryName}"/> interface. It is designed to fulfill the following
/// requirements:
/// <list type="bullet">
///     <item>
///         It should make setup and verification of a mock <see cref="ILogger{TCategoryName}"/> easy, regardless of the mocking
///         library.
///     </item>
///     <item>
///         As a base class for a custom <see cref="ILogger{TCategoryName}"/>, it should be both easy to implement and easy to
///         understand <em>how</em> to implement it.
///     </item>
///     <item>
///         It should correctly implement logging scopes and make this information easily available to a test or custom logger
///         implementation.
///     </item>
///     <item>
///         It should have minimal impact on performance.
///     </item>
/// </list>
/// </summary>
/// <typeparam name="TCategoryName">The type whose name is used for the logger category name.</typeparam>
public abstract class EasyLogger<TCategoryName> : EasyLogger, ILogger<TCategoryName>
{
}
