using System;
using System.Threading;
using System.Threading.Tasks;
using SsmsToolkit.Presentation.Localization;
using SsmsToolkit.Presentation.Mvvm;

namespace SsmsToolkit.Presentation.TableInspector
{
    public enum TabState
    {
        NotLoaded,
        Loading,
        Loaded,
        Error,
    }

    /// <summary>A tab that loads asynchronously, can be cancelled and shows its own error.</summary>
    public abstract class InspectorTab : ObservableObject
    {
        private readonly CancellationToken _lifetime;
        private readonly Action<string, Exception> _reportError;
        private CancellationTokenSource _cts;
        private TabState _state;
        private string _errorMessage;

        protected InspectorTab(string header, CancellationToken lifetime, Action<string, Exception> reportError)
        {
            Header = header;
            _lifetime = lifetime;
            _reportError = reportError;
        }

        public string Header { get; }

        public TabState State
        {
            get => _state;
            private set
            {
                if (Set(ref _state, value))
                {
                    OnPropertyChanged(nameof(IsLoading));
                    OnPropertyChanged(nameof(HasError));
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public bool IsLoading => State == TabState.Loading;

        public bool HasError => State == TabState.Error;

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => Set(ref _errorMessage, value);
        }

        public event EventHandler StateChanged;

        public Task EnsureLoadedAsync() => State == TabState.NotLoaded ? ReloadAsync() : Task.CompletedTask;

        public async Task ReloadAsync()
        {
            Cancel();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
            _cts = cts;
            ErrorMessage = null;
            State = TabState.Loading;
            try
            {
                await LoadCoreAsync(cts.Token);
                if (_cts == cts)
                {
                    State = TabState.Loaded;
                }
            }
            catch (OperationCanceledException)
            {
                if (_cts == cts)
                {
                    ErrorMessage = Strings.Get("Common_Cancelled");
                    State = TabState.Error;
                }
            }
            catch (Exception ex)
            {
                if (_cts == cts)
                {
                    // SqlException etc.: show the message; it never contains the password.
                    ErrorMessage = ex is InvalidOperationException || ex.GetType().Name == "SqlException"
                        ? ex.Message
                        : ex.GetType().Name + ": " + ex.Message;
                    State = TabState.Error;
                }
                _reportError?.Invoke(Header, ex);
            }
        }

        public void Cancel()
        {
            var cts = _cts;
            if (cts != null && State == TabState.Loading)
            {
                cts.Cancel();
            }
        }

        protected abstract Task LoadCoreAsync(CancellationToken ct);
    }
}
