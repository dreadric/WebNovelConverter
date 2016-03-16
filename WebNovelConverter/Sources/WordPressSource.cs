﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Dom.Html;
using AngleSharp.Extensions;
using AngleSharp.Parser.Html;
using WebNovelConverter.Extensions;
using WebNovelConverter.Sources.Models;

namespace WebNovelConverter.Sources
{
    public class WordPressSource : WebNovelSource
    {
        protected readonly List<string> BloatClasses = new List<string>
        {
            "sharedaddy",
            "share-story-container",
            "code-block",
            "comments-area",
            "pagination"
        };

        protected readonly List<string> PageClasses = new List<string>
        {
            "post-entry",
            "entry-content",
            "post-content",
            "the-content",
            "entry",
            "page-body"
        };

        protected readonly List<string> PostClasses = new List<string>
        {
            "post-entry",
            "entry-content",
            "post-content",
            "postbody",
            "page-body",
            "post",
            "hentry"
        };

        protected readonly List<string> PaginationClasses = new List<string>
        {
            "pagination"
        };

        protected readonly List<string> TitleClasses = new List<string>
        {
            "entry-title",
            "post-title",
            "page-title"
        };

        protected readonly List<string> NextChapterNames = new List<string>
        {
            "Next Chapter",
            "Next Page",
            "Next"
        };

        public WordPressSource() : base("WordPress")
        {
        }

        public WordPressSource(string type) : base(type)
        {
        }

        public override async Task<IEnumerable<ChapterLink>> GetChapterLinksAsync(string baseUrl, CancellationToken token = default(CancellationToken))
        {
            string baseContent = await GetWebPageAsync(baseUrl, token);

            IHtmlDocument doc = await Parser.ParseAsync(baseContent, token);

            var divElements = from e in doc.All
                              where e.LocalName == "div"
                              where e.HasAttribute("class")
                              let names = e.GetAttribute("class").Split(' ')
                              from name in names
                              where PageClasses.Any(p => p.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                              select e;

            IElement element = divElements.FirstOrDefault();

            if (element == null)
                return EmptyLinks;

            return CollectChapterLinks(baseUrl, element.Descendents<IElement>());
        }

        public override async Task<WebNovelChapter> GetChapterAsync(ChapterLink link,
            ChapterRetrievalOptions options = default(ChapterRetrievalOptions),
            CancellationToken token = default(CancellationToken))
        {
            string content = await GetWebPageAsync(link.Url, token);
            IHtmlDocument doc = await Parser.ParseAsync(content, token);

            var paged = GetPagedChapterUrls(doc.DocumentElement);

            WebNovelChapter chapter = ParseChapter(doc.DocumentElement, token);

            if (chapter == null)
                return null;

            chapter.Url = link.Url;

            foreach (var page in paged)
            {
                string pageContent = await GetWebPageAsync(page, token);

                IHtmlDocument pageDoc = await Parser.ParseAsync(pageContent, token);

                chapter.Content += ParseChapter(pageDoc.DocumentElement, token).Content;
            }

            return chapter;
        }

        protected virtual WebNovelChapter ParseChapter(IElement rootElement, CancellationToken token = default(CancellationToken))
        {
            IElement element = rootElement.FirstWhereHasClass(PostClasses)
                                    ?? rootElement.Descendents<IElement>().FirstOrDefault(p => p.LocalName == "article");

            if (element != null)
                RemoveBloat(element);

            IElement chapterNameElement = rootElement.FirstWhereHasClass(TitleClasses);

            if (element != null && chapterNameElement == null)
            {
                chapterNameElement = (from e in element.Descendents<IElement>()
                                      where e.LocalName == "h1" || e.LocalName == "h2"
                                        || e.LocalName == "h3" || e.LocalName == "h4"
                                      select e).FirstOrDefault();
            }
            else
            {
                IElement chNameLinkElement = (from e in chapterNameElement.Descendents<IElement>()
                                              where e.LocalName == "a"
                                              select e).FirstOrDefault();

                if (chNameLinkElement != null)
                    chapterNameElement = chNameLinkElement;
            }

            IElement nextChapterElement = (from e in rootElement.Descendents<IElement>() ?? new List<IElement>()
                                           where e.LocalName == "a"
                                           let text = e.Text()
                                           let a = NextChapterNames.FirstOrDefault(p => text.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                                           where a != null || (e.HasAttribute("rel") && e.GetAttribute("rel") == "next")
                                           orderby NextChapterNames.IndexOf(a)
                                           select e).FirstOrDefault();

            WebNovelChapter chapter = new WebNovelChapter();
            if (nextChapterElement != null)
            {
                chapter.NextChapterUrl = nextChapterElement.GetAttribute("href");
            }

            if (element != null)
            {
                RemoveNavigation(element);

                chapter.ChapterName = chapterNameElement?.Text()?.Trim();
                chapter.Content = element.InnerHtml;
            }
            else
            {
                chapter.Content = "No Content";
            }

            return chapter;
        }

        public override Task<string> GetNovelCoverAsync(string baseUrl, CancellationToken token = new CancellationToken())
        {
            return Task.FromResult(string.Empty);
        }

        protected virtual IEnumerable<string> GetPagedChapterUrls(IElement rootElement)
        {
            var pagElements = rootElement.FirstWhereHasClass(PaginationClasses, e => e.LocalName == "div")
                ?.Descendents<IElement>();

            pagElements = pagElements?.Where(p => p.LocalName == "a");

            if (pagElements == null)
                return new List<string>();

            return pagElements.Select(p => p.GetAttribute("href"));
        }

        protected virtual void RemoveBloat(IElement element)
        {
            var shareElements = element.WhereHasClass(BloatClasses);

            foreach (IElement e in shareElements)
            {
                e.Remove();
            }
        }

        protected virtual void RemoveNavigation(IElement element)
        {
        }
    }
}
