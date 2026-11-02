using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ThaiNationalIDCard
{
    /// <summary>Reads Thai national ID cards. An interface so that consumers can mock the reader in their own tests.</summary>
    public interface IThaiIdCardReader
    {
        /// <summary>Lists the card readers PC/SC knows. This is a short blocking call.</summary>
        /// <returns>
        /// The reader names as PC/SC gives them, empty when there are none. A name can hold the serial of the device, so
        /// treat it like other device data and do not log it without thought.
        /// </returns>
        /// <exception cref="SmartCardServiceUnavailableException">PC/SC is not available.</exception>
        IReadOnlyList<string> GetReaderNames();

        /// <summary>Reads the card in one reader.</summary>
        /// <param name="readerName">
        /// A name from <see cref="GetReaderNames"/>, or the name as <see cref="ReadDiagnostics"/> shows it. Another reader is
        /// never read in its place.
        /// </param>
        /// <param name="options">What to read. Null means <see cref="ReadOptions"/> with its defaults.</param>
        /// <param name="cancellationToken">
        /// Cancels between card commands. A command already sent cannot be taken back, so cancelling takes effect when it ends.
        /// </param>
        /// <returns>What the card gave. Look at <see cref="ThaiIdCardData.Quality"/> per field.</returns>
        /// <exception cref="ThaiIdCardException">The read failed. The subclass says how and Diagnostics holds the trace.</exception>
        /// <exception cref="ReadCanceledException">The read was cancelled while it was running.</exception>
        /// <exception cref="System.OperationCanceledException">The token was already cancelled.</exception>
        /// <exception cref="System.ArgumentException">The reader name is null or empty.</exception>
        /// <exception cref="System.NotSupportedException">The options ask for a mode that is not available yet.</exception>
        Task<ThaiIdCardData> ReadAsync(string readerName, ReadOptions? options, CancellationToken cancellationToken);

        /// <summary>
        /// Reads the first reader, in PC/SC order, that holds a readable card, with the default options. A reader with no card
        /// or a mute card is passed over, and so is one whose card turns out not to be readable, such as a card of another kind.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels between card commands. A command already sent cannot be taken back, so cancelling takes effect when it ends.
        /// </param>
        /// <returns>What the card gave. Look at <see cref="ThaiIdCardData.Quality"/> per field.</returns>
        /// <exception cref="ThaiIdCardException">
        /// No card could be read. When several were tried this is the failure of the first one. Diagnostics holds its trace.
        /// </exception>
        /// <exception cref="ReadCanceledException">The read was cancelled while it was running.</exception>
        /// <exception cref="System.OperationCanceledException">The token was already cancelled.</exception>
        Task<ThaiIdCardData> ReadAsync(CancellationToken cancellationToken);
    }
}
