﻿using Microsoft.AspNetCore.Mvc;
using ShareBook.Api.ViewModels;
using ShareBook.Domain.Entities;
using ShareBook.Service;

namespace ShareBook.Api.Controllers
{
    [Route("api/[controller]")]
    public class CategoryController : BaseCrudController<Category>
    {
        public CategoryController(ICategoryService categoryService) : base(categoryService)
        {
            SetDefault(x => x.Name);
        }
    }
}
