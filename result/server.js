var express = require('express'),
    { Pool } = require('pg'),
    cookieParser = require('cookie-parser'),
    path = require('path'),
    app = express(),
    server = require('http').Server(app);

var port = process.env.PORT || 4000;

// Middleware para parsing de cookies e urlencoded
app.use(cookieParser());
app.use(express.urlencoded({ extended: true }));

// Middleware para servir arquivos estáticos corretamente
app.use('/result', express.static(path.join(__dirname, 'views')));

// Configuração da conexão com PostgreSQL
var pool = new Pool({
  connectionString: 'postgres://postgres:postgres@35.193.219.146:5432/postgres'
});

// Rota para página inicial (index.html)
app.get('/result', function (req, res) {
  res.sendFile(path.resolve(__dirname + '/views/index.html'));
});

// Rota para API REST para obter resultados
app.get('/result/api/results', function (req, res) {
  pool.query('SELECT vote, COUNT(id) AS count FROM votes GROUP BY vote', [], function(err, result) {
    if (err) {
      console.error("Error performing query:", err);
      res.status(500).json({ error: 'Internal server error' });
    } else {
      var votes = collectVotesFromResult(result);
      res.json({ data: votes });
    }
  });
});

// Função para coletar votos do resultado da query
function collectVotesFromResult(result) {
  var votes = { a: 0, b: 0 };

  result.rows.forEach(function (row) {
    votes[row.vote] = parseInt(row.count);
  });

  return votes;
}

// Iniciar o servidor
server.listen(port, function () {
  var port = server.address().port;
  console.log('App running on port ' + port);
});
