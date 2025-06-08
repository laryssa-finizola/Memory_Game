using Microsoft.AspNetCore.Mvc;
using server.Models;
using System.Collections.Generic;

namespace server.Controllers {
    [ApiController]
    [Route("api/[controller]")]
    public class RankingController : ControllerBase {
        private readonly RankingRepository _repository;

        public RankingController() {
            _repository = new RankingRepository();
        }

        [HttpGet("top5")]
        public ActionResult<List<RankingEntry>> GetTop5() {
            var top5 = _repository.ObterTop5();
            return Ok(top5);
        }
    }
}
