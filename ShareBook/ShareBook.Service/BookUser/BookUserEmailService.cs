﻿using Microsoft.Extensions.Caching.Memory;
using ShareBook.Domain;
using ShareBook.Domain.DTOs;
using ShareBook.Repository.Repository;
using ShareBook.Service.Notification;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ShareBook.Service
{
    public class BookUserEmailService : IBookUsersEmailService
    {
        private const string BookNoticeDonorTemplate = "BookNoticeDonorTemplate";
        private const string BookDonatedTemplate = "BookDonatedTemplate";
        private const string BookDonatedTemplateNotifyDonor = "BookDonatedNotifyDonorTemplate";
        private const string BookNoticeDeclinedUsersTemplate = "BookNoticeDeclinedUsersTemplate";
        private const string BookCanceledNoticeUsersTemplate = "BookCanceledNoticeUsersTemplate";
        private const string BookTrackingNumberNoticeWinnerTemplate = "BookTrackingNumberNoticeWinnerTemplate";
        private const string BookDonatedTitle = "Parabéns você foi selecionado!";
        private const string BookDonatedTitleNotifyDonor = "Parabéns você escolheu um ganhador!";
        private const string BookNoticeDonorTitle = "Seu livro foi solicitado!";
        private const string BookCanceledTemplate = "BookCanceledTemplate";
        private const string BookCanceledTitle = "Doação cancelada";
        private const string BookTrackingNumberNoticeWinnerTitle = "Seu livro foi postado - Sharebook";
        private const string BookNoticeInterestedTemplate = "BookNoticeInterestedTemplate";
        private const string BookNoticeInterestedTitle = "Sharebook - Você solicitou um livro";

        private readonly IUserService _userService;
        private readonly IBookService _bookService;
        private readonly IEmailService _emailService;
        private readonly IEmailTemplate _emailTemplate;
        private readonly INotificationService _notificationService;
        private IMemoryCache _cache;

        public BookUserEmailService(IUserService userService, IBookService bookService, IEmailService emailService, IEmailTemplate emailTemplate, INotificationService notificationService, IMemoryCache memoryCache)
        {
            _userService = userService;
            _bookService = bookService;
            _emailService = emailService;
            _emailTemplate = emailTemplate;
            _notificationService = notificationService;
            _cache = memoryCache;
        }

        public async Task SendEmailBookDonated(BookUser bookUser)
        {
            var bookDonated = bookUser.Book;
            if (bookDonated.User == null)
                bookDonated.User = _userService.Find(bookUser.Book.UserId);

            if (bookDonated.User.AllowSendingEmail)
            {
                var vm = new
                {
                    Book = bookDonated,
                    bookUser.User
                };
                var html = await _emailTemplate.GenerateHtmlFromTemplateAsync(BookDonatedTemplate, vm);
                await _emailService.Send(bookUser.User.Email, bookUser.User.Name, html, BookDonatedTitle, copyAdmins: false);
            }
        }

        public async Task SendEmailBookDonatedNotifyDonor(Book book, User winner)
        {
            if (book.User.AllowSendingEmail)
            {
                var vm = new
                {
                    BookTitle = book.Title,
                    DonorName = book.User.Name,
                    Facilitator = book.UserFacilitator,
                    Winner = winner
                };
                var html = await _emailTemplate.GenerateHtmlFromTemplateAsync(BookDonatedTemplateNotifyDonor, vm);

                // TODO: não enviar cópia para admins quando esse processo estiver bem amadurecido.
                await _emailService.Send(book.User.Email, book.User.Name, html, BookDonatedTitleNotifyDonor, copyAdmins: true);
            }
        }

        public async Task SendEmailBookDonor(BookUser bookUser, Book bookRequested)
        {
            // envia no máximo 1 email por hora. Pra não sobrecarregar o doador.
            if (!MaxEmailsDonorValid(bookRequested))
                return;

            //obter o endereço do interessado
            var donatedUser = this._userService.Find(bookUser.UserId);
            if (bookRequested.User.AllowSendingEmail)
            {
                var htmlTable = GenerateInterestedListHtml(bookRequested);

                var vm = new
                {
                    HtmlTable = htmlTable,
                    Donor = new
                    {
                        Name = bookRequested.User.Name,
                        ChooseDate = string.Format("{0:dd/MM/yyyy}", bookRequested.ChooseDate.Value),
                        BookTitle = bookRequested.Title,
                    },
                    RequestingUser = new { bookUser.NickName },

                };

                // push notification
                _notificationService.SendNotificationByEmail(
                    bookRequested.User.Email,
                    $"Seu livro foi solicitado", $" O Interessado é {vm.RequestingUser.NickName}"
                );

                var html = await _emailTemplate.GenerateHtmlFromTemplateAsync(BookNoticeDonorTemplate, vm);

                // TODO: remover cópia adm quando esse processo estiver amadurecido.
                await _emailService.Send(bookRequested.User.Email, bookRequested.User.Name, html, BookNoticeDonorTitle, copyAdmins: true);

                EmailsDonorAddCache(bookRequested);
            }
        }

        private string GenerateInterestedListHtml(Book bookRequested)
        {
            var html = "<table border=1 cellpadding=3 cellspacing=0>";
            html += "<tr><td bgcolor = '#ffff00'><b> APELIDO </b></td><td bgcolor = '#ffff00'><b> PEDIDO </b></td></tr>";

            var requests = bookRequested.BookUsers.Where(r => r.CreationDate >= DateTime.Now.AddMinutes(-180)).OrderByDescending(r => r.CreationDate);

            foreach (var request in requests)
            {
                html += "<tr><td>" + request.NickName + "</td><td><pre>" + request.Reason + "</pre></td></tr>";
            }

            html += "<tr><td colspan=\"2\"> Para ver a lista completa de interessados, use esse link: <a href=\"https://www.sharebook.com.br/book/donate/" + bookRequested.Slug + "?returnUrl=book%2Fdonations\">" + bookRequested.Title + "</a>.</td></tr>";
            html += "</table>";

            return html;
        }

        private bool MaxEmailsDonorValid(Book bookRequested)
        {
            var key = $"BookRequested_{bookRequested.UserId}_{bookRequested.Id}";
            var hasCache = _cache.TryGetValue(key, out bool value);
            return !hasCache;
        }

        private void EmailsDonorAddCache(Book bookRequested)
        {
            var key = $"BookRequested_{bookRequested.UserId}_{bookRequested.Id}";

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(60));

            _cache.Set(key, true, cacheOptions);
        }

        public async Task SendEmailBookInterested(BookUser bookUser, Book book)
        {
            // lazy load depression
            if (bookUser.User == null)
                bookUser.User = _userService.Find(bookUser.UserId);

            if (bookUser.User.AllowSendingEmail)
            {
                var vm = new
                {
                    NameBook = bookUser.Book.Title,
                    NameFacilitator = book.UserFacilitator.Name,
                    LinkedinFacilitator = book.UserFacilitator.Linkedin,
                    PhoneFacilitator = book.UserFacilitator.Phone,
                    EmailFacilitator = book.UserFacilitator.Email,
                    ChooseDate = string.Format("{0:dd/MM/yyyy}", book.ChooseDate.Value) ,
                    NameInterested = bookUser.User.Name,
                };

                // push notification
                _notificationService.SendNotificationByEmail(bookUser.User.Email, $"Você solicitou o livro {vm.NameBook}", $"Aguarde até o dia {vm.ChooseDate} que será anunciado o ganhador. Boa sorte!");

                var html = await _emailTemplate.GenerateHtmlFromTemplateAsync(BookNoticeInterestedTemplate, vm);
                await _emailService.Send(bookUser.User.Email, bookUser.User.Name, html, BookNoticeInterestedTitle);
            }
        }

        /// <summary>
        /// Metodo tem como finalizado fazer tratativas na geração da informação de localidade que será enviado doador
        /// </summary>
        /// <param name="donatedUser"></param>
        /// <returns></returns>
        private string GenerateDonatedLocation(User donatedUser)
        {
            string ND = "N/D";
            if (donatedUser == null) return ND;

            if (donatedUser.Address == null) return ND;


            var address = donatedUser.Address;
            string location = string.Empty;

            if (!string.IsNullOrEmpty(address.City))
                location = address.City.ToUpper();

            if (!string.IsNullOrEmpty(address.State))
                location += $"/{address.State}";            

            return location;

        }

        public async Task SendEmailDonationDeclined(Book book, BookUser bookUserWinner, List<BookUser> bookUsersDeclined)
        {
            var vm = new
            {
                BookTitle = book.Title,
            };
            var html = await _emailTemplate.GenerateHtmlFromTemplateAsync(BookNoticeDeclinedUsersTemplate, vm);
            var emailSubject = $"Resultado da doação do livro {book.Title}.";

            bookUsersDeclined.ForEach(bookUser =>
            {
                if (bookUser.User.AllowSendingEmail)
                    _emailService.Send(bookUser.User.Email, bookUser.User.Name, html, emailSubject).Wait();
            });

        }

        public async Task SendEmailDonationCanceled(Book book, List<BookUser> bookUsers)
        {
            var vm = new { book };
            var html = await _emailTemplate.GenerateHtmlFromTemplateAsync(BookCanceledNoticeUsersTemplate, vm);

            bookUsers.ForEach(bookUser =>
            {
                if (bookUser.User.AllowSendingEmail)
                    _emailService.Send(bookUser.User.Email, bookUser.User.Name, html, $"Resultado da doação do livro {book.Title}.").Wait();
            });
            
        }

        public async Task SendEmailBookCanceledToAdminsAndDonor(BookCancelationDTO dto)
        {
            var donor = dto.Book.User;

            var templateData = new
            {
                Title = dto.Book.Title,
                Donor = donor.Name,
                CanceledBy = dto.CanceledBy,
                Reason = dto.Reason
            };

            var html = await _emailTemplate.GenerateHtmlFromTemplateAsync(BookCanceledTemplate, templateData);
            _emailService.Send(donor.Email, donor.Name, html, BookCanceledTitle, copyAdmins: true).Wait();
        }
    
        public async Task SendEmailTrackingNumberInformed(BookUser bookUserWinner, Book book)
        {
            if (bookUserWinner.User.AllowSendingEmail)
            {
                var vm = new
                {
                    book = book,
                    NameFacilitator = book.UserFacilitator.Name,
                    LinkedInFacilitator = book.UserFacilitator.Linkedin,
                    ZapFacilitator = book.UserFacilitator.Phone,
                    EmailFacilitator = book.UserFacilitator.Email,
                };
                var html = await _emailTemplate.GenerateHtmlFromTemplateAsync(BookTrackingNumberNoticeWinnerTemplate, vm);
                await _emailService.Send(bookUserWinner.User.Email, bookUserWinner.User.Name, html, BookTrackingNumberNoticeWinnerTitle, copyAdmins: false);
            }
        }

        public async Task SendEmailMaxRequests(Book bookRequested)
        {
            var subject = "Limite de pedidos";
            var body = $"Prezados adms, o livro <b>{bookRequested.Title}</b> atingiu o limite de pedidos e foi removido automaticamente da vitrine. A data de decisão foi configurada pra amanhã. Obrigado.";
            await _emailService.SendToAdmins(body, subject);
        }
    }
}
